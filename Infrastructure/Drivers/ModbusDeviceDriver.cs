using System.Buffers.Binary;
using System.IO.Ports;
using System.Text.Json;
using Core.Acquisition;
using Core.DTOs;
using Core.Enums;
using Core.Interface;
using FluentModbus;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Drivers
{
	/// <summary>
	/// Driver Modbus TCP dan RTU.
	///
	/// TIGA KEPUTUSAN YANG MENENTUKAN APAKAH DRIVER INI BISA DIPAKAI DI PABRIK:
	///
	/// 1. PEMBACAAN DIGABUNG PER BLOK REGISTER. Modbus tidak punya "baca 200 alamat ini";
	///    yang ada adalah "baca N register berurutan mulai dari X". Membaca 200 tag satu per
	///    satu berarti 200 perjalanan pulang-balik per siklus — pada serial 9600 bps itu
	///    puluhan detik untuk pekerjaan yang seharusnya di bawah satu detik. Di sini alamat
	///    yang berdekatan disatukan menjadi satu permintaan, dengan batas 125 register
	///    (batas frame Modbus).
	///
	/// 2. URUTAN WORD BISA DIATUR. Modbus membakukan urutan byte di dalam register, tetapi
	///    TIDAK membakukan urutan dua register yang membentuk satu angka 32-bit. Separuh
	///    vendor memakai word tinggi lebih dulu, separuh sebaliknya. Salah pilih tidak
	///    menghasilkan galat — ia menghasilkan angka yang masuk akal tetapi salah (mis. 1.2
	///    menjadi 4,6×10³⁷). Karena itu ini setelan per perangkat, bukan asumsi.
	///
	/// 3. SATU BLOK GAGAL TIDAK MEMBUTAKAN SELURUH PERANGKAT. Rentang register yang tidak
	///    ada di PLC dijawab exception Modbus. Tag di blok itu ditandai Bad; tag di blok lain
	///    tetap terbaca. Menggagalkan semuanya berarti satu alamat salah ketik mematikan
	///    seluruh mesin dari pandangan operator.
	/// </summary>
	public sealed class ModbusDeviceDriver : IDeviceDriver
	{
		/// <summary>Batas jumlah register per permintaan menurut spesifikasi Modbus.</summary>
		private const int MaxRegistersPerRequest = 125;

		/// <summary>Batas jumlah bit per permintaan.</summary>
		private const int MaxBitsPerRequest = 2000;

		/// <summary>
		/// Jarak maksimum antar alamat yang masih pantas disatukan dalam satu blok. Membaca
		/// beberapa register yang tidak dipakai jauh lebih murah daripada satu perjalanan
		/// pulang-balik tambahan; tetapi menjembatani lubang besar berarti mengangkut ratusan
		/// register yang tidak ada gunanya.
		/// </summary>
		private const int MaxGapWithinBlock = 8;

		private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

		private readonly DevicePlan _plan;
		private readonly ILogger<ModbusDeviceDriver> _logger;
		private readonly ModbusClient _client;
		private readonly byte _unitId;
		private readonly bool _wordSwap;
		private readonly string _target;
		private readonly SemaphoreSlim _mutex = new(1, 1);

		private bool _connected;
		private string? _lastError;
		private DateTime? _lastSuccessAt;
		private int _consecutiveFailures;

		public ModbusDeviceDriver(DevicePlan plan, ILogger<ModbusDeviceDriver> logger)
		{
			_plan = plan;
			_logger = logger;

			if (plan.Protocol == Protocol.MODBUS_TCP)
			{
				var config = Deserialize<ModbusTcpConfig>(plan.ConnectionConfigJson)
					?? throw new InvalidOperationException($"Konfigurasi Modbus TCP perangkat {plan.DeviceName} tidak bisa dibaca");

				var tcp = new ModbusTcpClient
				{
					ConnectTimeout = Math.Clamp(config.ConnectionTimeout, 500, 30_000),
					ReadTimeout = Math.Clamp(config.ConnectionTimeout, 500, 30_000),
					WriteTimeout = Math.Clamp(config.ConnectionTimeout, 500, 30_000)
				};

				_client = tcp;
				_unitId = (byte)Math.Clamp(config.SlaveId, 0, 255);
				_wordSwap = config.WordSwap;
				_target = $"{config.IPAddress}:{config.Port}";
			}
			else
			{
				var config = Deserialize<ModbusRtuConfig>(plan.ConnectionConfigJson)
					?? throw new InvalidOperationException($"Konfigurasi Modbus RTU perangkat {plan.DeviceName} tidak bisa dibaca");

				var rtu = new ModbusRtuClient
				{
					BaudRate = config.BaudRate,
					Parity = ParseParity(config.Parity),
					StopBits = config.StopBits switch { 0 => StopBits.None, 2 => StopBits.Two, 3 => StopBits.OnePointFive, _ => StopBits.One },
					ReadTimeout = 2_000,
					WriteTimeout = 2_000
				};

				_client = rtu;
				_unitId = (byte)Math.Clamp(config.SlaveId, 0, 255);
				_wordSwap = config.WordSwap;
				_target = config.PortName;
			}
		}

		public Protocol Protocol => _plan.Protocol;
		public Guid DeviceId => _plan.DeviceId;

		public DriverHealth Health => new()
		{
			IsConnected = _connected,
			LastError = _lastError,
			LastSuccessAt = _lastSuccessAt,
			ConsecutiveFailures = Volatile.Read(ref _consecutiveFailures)
		};

		public Task ConnectAsync(CancellationToken ct)
		{
			if (_connected) return Task.CompletedTask;

			try
			{
				switch (_client)
				{
					case ModbusTcpClient tcp:
						var parts = _target.Split(':');
						tcp.Connect(new System.Net.IPEndPoint(
							System.Net.IPAddress.Parse(parts[0]),
							parts.Length > 1 ? int.Parse(parts[1]) : 502),
							ModbusEndianness.BigEndian);
						break;

					case ModbusRtuClient rtu:
						rtu.Connect(_target, ModbusEndianness.BigEndian);
						break;
				}

				_connected = true;
				_lastError = null;
				_logger.LogInformation("Modbus tersambung ke {Target} (unit {Unit})", _target, _unitId);
			}
			catch (Exception ex)
			{
				_connected = false;
				_lastError = ex.Message;
				throw;
			}

			return Task.CompletedTask;
		}

		/// <summary>Modbus adalah protokol tarik; tidak ada langganan.</summary>
		public Task SubscribeAsync(
			IReadOnlyList<TagPlan> tags,
			Func<TagSample, CancellationToken, Task> onSample,
			CancellationToken ct) => Task.CompletedTask;

		public async Task<IReadOnlyList<TagSample>> ReadAsync(IReadOnlyList<TagPlan> tags, CancellationToken ct)
		{
			var now = DateTime.UtcNow;
			var results = new List<TagSample>(tags.Count);

			// Alamat diurai lebih dulu. Alamat yang tidak sah adalah kesalahan KONFIGURASI, dan
			// dilaporkan sebagai itu — bukan sebagai perangkat yang bermasalah, yang akan
			// mengirim teknisi memeriksa kabel yang sehat.
			var parsed = new List<(TagPlan Tag, ModbusAddress Address, int Span)>();

			foreach (var tag in tags)
			{
				if (tag.DataType == DataType.STRING)
				{
					results.Add(TagSample.Failed(tag.TagId, tag.DeviceId,
						"Tipe STRING belum didukung driver Modbus", now));
					continue;
				}

				if (!ModbusAddress.TryParse(tag.Address, out var address, out var error))
				{
					results.Add(TagSample.Failed(tag.TagId, tag.DeviceId,
						$"Alamat tag tidak sah: {error}", now));
					continue;
				}

				parsed.Add((tag, address, ModbusAddress.RegisterSpan(tag.DataType)));
			}

			if (parsed.Count == 0) return results;

			await _mutex.WaitAsync(ct);
			try
			{
				if (!_connected) await ConnectAsync(ct);

				foreach (var kindGroup in parsed.GroupBy(p => p.Address.Kind))
				{
					foreach (var block in BuildBlocks(kindGroup.ToList(), kindGroup.Key))
					{
						ct.ThrowIfCancellationRequested();
						await ReadBlockAsync(kindGroup.Key, block, results, now, ct);
					}
				}

				if (results.Any(r => r.Quality != Quality.Bad))
				{
					_lastSuccessAt = DateTime.UtcNow;
					Interlocked.Exchange(ref _consecutiveFailures, 0);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				// Kegagalan tingkat koneksi: seluruh tag yang belum terjawab menjadi Bad, dan
				// koneksi ditandai putus supaya siklus berikutnya menyambung ulang.
				_connected = false;
				_lastError = ex.Message;
				Interlocked.Increment(ref _consecutiveFailures);

				var answered = results.Select(r => r.TagId).ToHashSet();
				foreach (var (tag, _, _) in parsed.Where(p => !answered.Contains(p.Tag.TagId)))
				{
					results.Add(TagSample.Failed(tag.TagId, tag.DeviceId, ex.Message, now));
				}
			}
			finally
			{
				_mutex.Release();
			}

			return results;
		}

		/* ---------------------------- penggabungan blok ---------------------------- */

		private sealed record Block(int Start, int Count, List<(TagPlan Tag, ModbusAddress Address, int Span)> Tags);

		private static IEnumerable<Block> BuildBlocks(
			List<(TagPlan Tag, ModbusAddress Address, int Span)> items, RegisterKind kind)
		{
			var maxPerRequest = kind is RegisterKind.Coil or RegisterKind.DiscreteInput
				? MaxBitsPerRequest
				: MaxRegistersPerRequest;

			var sorted = items.OrderBy(i => i.Address.Offset).ToList();
			var current = new List<(TagPlan, ModbusAddress, int)>();
			var start = 0;
			var end = 0;

			foreach (var item in sorted)
			{
				var itemEnd = item.Address.Offset + (kind is RegisterKind.Coil or RegisterKind.DiscreteInput ? 1 : item.Span);

				if (current.Count == 0)
				{
					current.Add(item);
					start = item.Address.Offset;
					end = itemEnd;
					continue;
				}

				var fitsGap = item.Address.Offset - end <= MaxGapWithinBlock;
				var fitsSize = itemEnd - start <= maxPerRequest;

				if (fitsGap && fitsSize)
				{
					current.Add(item);
					end = Math.Max(end, itemEnd);
					continue;
				}

				yield return new Block(start, end - start, [..current]);
				current = [item];
				start = item.Address.Offset;
				end = itemEnd;
			}

			if (current.Count > 0) yield return new Block(start, end - start, [..current]);
		}

		private async Task ReadBlockAsync(
			RegisterKind kind, Block block, List<TagSample> results, DateTime now, CancellationToken ct)
		{
			try
			{
				var data = kind switch
				{
					RegisterKind.Holding => await _client.ReadHoldingRegistersAsync(_unitId, (ushort)block.Start, (ushort)block.Count, ct),
					RegisterKind.Input => await _client.ReadInputRegistersAsync(_unitId, (ushort)block.Start, (ushort)block.Count, ct),
					RegisterKind.Coil => await _client.ReadCoilsAsync(_unitId, block.Start, block.Count, ct),
					_ => await _client.ReadDiscreteInputsAsync(_unitId, block.Start, block.Count, ct)
				};

				var bytes = data.ToArray();

				foreach (var (tag, address, span) in block.Tags)
				{
					results.Add(Decode(tag, address, span, block.Start, bytes, now));
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (ModbusException ex)
			{
				// Exception PROTOKOL (alamat tidak ada, fungsi tidak didukung): perangkatnya
				// sehat dan menjawab, konfigurasinya yang salah. Blok ini gagal, blok lain jalan.
				_logger.LogWarning(
					"Modbus {Target} menolak blok {Kind} {Start}+{Count}: {Error}",
					_target, kind, block.Start, block.Count, ex.Message);

				foreach (var (tag, _, _) in block.Tags)
				{
					results.Add(TagSample.Failed(tag.TagId, tag.DeviceId,
						$"Perangkat menolak alamat: {ex.Message}", now));
				}
			}
		}

		/* -------------------------------- pembacaan -------------------------------- */

		private TagSample Decode(
			TagPlan tag, ModbusAddress address, int span, int blockStart, byte[] bytes, DateTime now)
		{
			try
			{
				if (address.IsBit)
				{
					var bitIndex = address.Offset - blockStart;
					var byteIndex = bitIndex / 8;

					if (byteIndex >= bytes.Length)
						return TagSample.Failed(tag.TagId, tag.DeviceId, "Respons lebih pendek dari yang diminta", now);

					var bit = (bytes[byteIndex] >> (bitIndex % 8) & 1) == 1;

					return new TagSample
					{
						TagId = tag.TagId,
						DeviceId = tag.DeviceId,
						Boolean = bit,
						Numeric = bit ? 1 : 0,
						Raw = bit ? 1 : 0,
						SourceTs = now,
						GatewayTs = now,
						Quality = Quality.Good
					};
				}

				var offsetBytes = (address.Offset - blockStart) * 2;
				if (offsetBytes + span * 2 > bytes.Length)
					return TagSample.Failed(tag.TagId, tag.DeviceId, "Respons lebih pendek dari yang diminta", now);

				var slice = bytes.AsSpan(offsetBytes, span * 2);

				double value;
				switch (tag.DataType)
				{
					case DataType.INT16:
						value = BinaryPrimitives.ReadInt16BigEndian(slice);
						break;

					case DataType.UINT16:
						value = BinaryPrimitives.ReadUInt16BigEndian(slice);
						break;

					case DataType.BOOLEAN:
						value = BinaryPrimitives.ReadUInt16BigEndian(slice) != 0 ? 1 : 0;
						break;

					case DataType.INT32:
						value = BitConverter.ToInt32(Order32(slice));
						break;

					case DataType.UINT32:
						value = BitConverter.ToUInt32(Order32(slice));
						break;

					case DataType.FLOAT:
						value = BitConverter.ToSingle(Order32(slice));
						break;

					default:
						return TagSample.Failed(tag.TagId, tag.DeviceId,
							$"Tipe data {tag.DataType} belum didukung driver Modbus", now);
				}

				return new TagSample
				{
					TagId = tag.TagId,
					DeviceId = tag.DeviceId,
					Numeric = value,
					Boolean = tag.DataType == DataType.BOOLEAN ? value != 0 : null,
					Raw = value,
					SourceTs = now,
					GatewayTs = now,
					Quality = Quality.Good
				};
			}
			catch (Exception ex)
			{
				return TagSample.Failed(tag.TagId, tag.DeviceId, $"Gagal menerjemahkan nilai: {ex.Message}", now);
			}
		}

		/// <summary>
		/// Menyusun empat byte 32-bit ke urutan little-endian yang dibutuhkan
		/// <see cref="BitConverter"/>, sekaligus menerapkan pertukaran word bila perangkat
		/// mengirim word rendah lebih dulu.
		///
		/// Di kabel, tiap register sudah big-endian (itu dibakukan). Yang tidak dibakukan adalah
		/// register mana yang membawa 16 bit atas — dan itulah yang diputuskan
		/// <c>wordSwap</c>.
		/// </summary>
		private byte[] Order32(ReadOnlySpan<byte> slice)
		{
			var high = _wordSwap ? slice[2..4] : slice[0..2];
			var low = _wordSwap ? slice[0..2] : slice[2..4];

			// Hasil akhir little-endian: byte paling tidak signifikan lebih dulu.
			return [low[1], low[0], high[1], high[0]];
		}

		private static T? Deserialize<T>(string json)
		{
			try
			{
				return JsonSerializer.Deserialize<T>(json, JsonOptions);
			}
			catch (JsonException)
			{
				return default;
			}
		}

		private static Parity ParseParity(string? parity) => parity?.Trim().ToUpperInvariant() switch
		{
			"ODD" => Parity.Odd,
			"EVEN" => Parity.Even,
			"MARK" => Parity.Mark,
			"SPACE" => Parity.Space,
			_ => Parity.None
		};

		public ValueTask DisposeAsync()
		{
			try
			{
				switch (_client)
				{
					case ModbusTcpClient tcp when tcp.IsConnected:
						tcp.Disconnect();
						break;
					case ModbusRtuClient rtu when rtu.IsConnected:
						rtu.Close();
						break;
				}
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Gagal menutup koneksi Modbus {Target}", _target);
			}

			_connected = false;
			_mutex.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
