using System.Buffers.Binary;
using System.Text;
using Core.Acquisition;
using Core.Interface;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Acquisition
{
	/// <summary>
	/// Write-ahead log berbasis berkas untuk sampel akuisisi.
	///
	/// KENAPA BERKAS, BUKAN REDIS/KAFKA/SQLITE:
	///
	/// Yang dibutuhkan hanya satu hal — sampel yang sudah dibaca dari perangkat tidak boleh
	/// hilang saat database atau proses mati. Untuk satu gateway, berkas append-only di disk
	/// lokal memberi jaminan itu dengan nol dependensi tambahan dan nol proses tambahan yang
	/// bisa mati. Broker justru menambah satu komponen lagi yang harus hidup agar data
	/// selamat, sementara masalah yang diselesaikannya (fan-out ke banyak konsumen, banyak
	/// lokasi) belum ada di sini.
	///
	/// FORMAT — biner dengan panjang di depan, bukan JSON:
	///
	///   [int32 payloadLength][payload]
	///
	/// Satu sampel memakan ~60 byte. JSON untuk data yang sama ~150 byte, dan pada 1.000
	/// sampel/detik selisihnya 7,8 GB per hari — pada gateway yang disknya diukur dalam
	/// belasan gigabyte, itu perbedaan antara tahan 24 jam dan tahan 8 jam.
	///
	/// PROTOKOL KOMIT:
	///
	/// Offset komit disimpan di berkas terpisah. Saat start, pembacaan dimulai dari offset itu,
	/// sehingga batch yang sempat dibaca tapi belum dikonfirmasi historian akan dibaca ulang.
	/// Pengulangan itu aman karena historian memakai kunci idempoten <c>(tag_id, source_ts)</c>
	/// — tanpa kunci itu, "tidak hilang" berubah menjadi "terhitung dua kali".
	///
	/// BATAS DURABILITAS YANG HARUS DIKETAHUI:
	///
	/// <c>fsync</c> dilakukan per flush, bukan per sampel. Dengan
	/// <c>flushIntervalMs = 500</c>, mati listrik mendadak bisa menghilangkan sampel dalam
	/// jendela ≤500 ms terakhir. Per-sampel <c>fsync</c> menurunkan throughput ke orde ratusan
	/// per detik — tidak sepadan. Setel <c>flushIntervalMs = 0</c> bila jendela itu tidak bisa
	/// diterima, dan sediakan UPS (yang memang sudah disyaratkan rancangan).
	/// </summary>
	public sealed class FileSampleBuffer : ISampleBuffer
	{
		private const int RecordHeaderSize = sizeof(int);
		private const byte FlagNumeric = 1 << 0;
		private const byte FlagBoolean = 1 << 1;
		private const byte FlagText = 1 << 2;
		private const byte FlagRaw = 1 << 3;

		private readonly string _dataPath;
		private readonly string _offsetPath;
		private readonly ILogger<FileSampleBuffer>? _logger;
		private readonly int _flushIntervalMs;
		private readonly long _compactThresholdBytes;

		private readonly SemaphoreSlim _gate = new(1, 1);
		private FileStream _data;
		private long _committedOffset;
		private DateTime _lastFlush = DateTime.UtcNow;
		private long _appended;
		private long _committed;
		private bool _dirty;

		public FileSampleBuffer(
			string directory,
			ILogger<FileSampleBuffer>? logger = null,
			int flushIntervalMs = 500,
			long compactThresholdBytes = 64L * 1024 * 1024)
		{
			Directory.CreateDirectory(directory);
			_dataPath = Path.Combine(directory, "samples.wal");
			_offsetPath = Path.Combine(directory, "samples.offset");
			_logger = logger;
			_flushIntervalMs = Math.Max(0, flushIntervalMs);
			_compactThresholdBytes = compactThresholdBytes;

			_data = OpenData();
			_committedOffset = ReadOffset();

			// Offset yang melebihi panjang berkas berarti berkas data hilang atau terpotong
			// (disk penuh, salin manual, berkas dihapus) sementara berkas offset tertinggal.
			// Menerimanya apa adanya akan membuat seluruh pembacaan berikutnya melewati data
			// yang sebenarnya ada.
			if (_committedOffset > _data.Length)
			{
				_logger?.LogWarning(
					"Offset komit ({Offset}) melebihi panjang WAL ({Length}); direset ke awal berkas",
					_committedOffset, _data.Length);
				_committedOffset = 0;
				WriteOffset(0);
			}

			if (_committedOffset > 0 && _committedOffset < _data.Length)
			{
				_logger?.LogInformation(
					"WAL memuat {Bytes} byte yang belum dikomit; akan diputar ulang",
					_data.Length - _committedOffset);
			}
		}

		private FileStream OpenData() => new(
			_dataPath,
			FileMode.OpenOrCreate,
			FileAccess.ReadWrite,
			FileShare.Read,
			bufferSize: 64 * 1024,
			FileOptions.None);

		/* ------------------------------------------------------------------ append */

		public async Task AppendAsync(IReadOnlyList<TagSample> samples, CancellationToken ct = default)
		{
			if (samples.Count == 0) return;

			await _gate.WaitAsync(ct);
			try
			{
				_data.Seek(0, SeekOrigin.End);

				foreach (var sample in samples)
				{
					var payload = Encode(sample);
					var header = new byte[RecordHeaderSize];
					BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

					await _data.WriteAsync(header, ct);
					await _data.WriteAsync(payload, ct);
				}

				_appended += samples.Count;
				_dirty = true;

				if (_flushIntervalMs == 0 ||
					(DateTime.UtcNow - _lastFlush).TotalMilliseconds >= _flushIntervalMs)
				{
					await FlushCoreAsync(ct);
				}
			}
			finally
			{
				_gate.Release();
			}
		}

		public async Task FlushAsync(CancellationToken ct = default)
		{
			await _gate.WaitAsync(ct);
			try
			{
				await FlushCoreAsync(ct);
			}
			finally
			{
				_gate.Release();
			}
		}

		private async Task FlushCoreAsync(CancellationToken ct)
		{
			if (!_dirty) return;

			await _data.FlushAsync(ct);
			// Flush(true) menembus cache OS. Tanpa argumen ini, "flush" hanya memindahkan data
			// dari buffer .NET ke buffer OS — dan mati listrik menghilangkan keduanya.
			_data.Flush(flushToDisk: true);
			_lastFlush = DateTime.UtcNow;
			_dirty = false;
		}

		/* -------------------------------------------------------------------- read */

		public async Task<SampleBatch> ReadBatchAsync(int maxSamples, CancellationToken ct = default)
		{
			await _gate.WaitAsync(ct);
			try
			{
				// Data yang belum di-flush tetap ada di buffer tulis; membacanya tanpa flush
				// akan melewatkan sampel terbaru.
				await FlushCoreAsync(ct);

				var samples = new List<TagSample>(Math.Min(maxSamples, 1024));
				var position = _committedOffset;
				var length = _data.Length;

				_data.Seek(position, SeekOrigin.Begin);
				var header = new byte[RecordHeaderSize];

				while (samples.Count < maxSamples && position < length)
				{
					if (!await ReadExactAsync(header, ct)) break;

					var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);

					// Panjang tidak masuk akal berarti berkas terpotong di tengah penulisan
					// (mati listrik saat append). Sisanya dibuang, bukan dipaksa dibaca:
					// membaca sampah sebagai sampel akan menuliskan nilai palsu ke historian.
					if (payloadLength <= 0 || position + RecordHeaderSize + payloadLength > length)
					{
						_logger?.LogWarning(
							"WAL terpotong pada offset {Offset}; {Bytes} byte terakhir dibuang",
							position, length - position);
						break;
					}

					var payload = new byte[payloadLength];
					if (!await ReadExactAsync(payload, ct)) break;

					samples.Add(Decode(payload));
					position += RecordHeaderSize + payloadLength;
				}

				return new SampleBatch { Samples = samples, CommitToken = position };
			}
			finally
			{
				_gate.Release();
			}
		}

		private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken ct)
		{
			var read = 0;
			while (read < buffer.Length)
			{
				var n = await _data.ReadAsync(buffer.AsMemory(read), ct);
				if (n == 0) return false;
				read += n;
			}
			return true;
		}

		/* ------------------------------------------------------------------ commit */

		public async Task CommitAsync(long commitToken, CancellationToken ct = default)
		{
			await _gate.WaitAsync(ct);
			try
			{
				// Token yang lebih kecil dari offset sekarang berarti komit ganda atau komit
				// dari batch lama; mengabaikannya lebih aman daripada memundurkan offset dan
				// mengirim ulang data yang sudah tersimpan.
				if (commitToken <= _committedOffset) return;

				_committedOffset = Math.Min(commitToken, _data.Length);
				WriteOffset(_committedOffset);
				_committed = _appended;

				// Seluruh isi sudah dikomit dan berkasnya besar: dipangkas supaya WAL tidak
				// tumbuh tanpa batas selama sistem berjalan sehat. Pemangkasan HANYA saat
				// tidak ada yang tertunda — memangkas di tengah akan menggeser semua offset.
				if (_committedOffset >= _data.Length && _data.Length >= _compactThresholdBytes)
				{
					await CompactAsync(ct);
				}
			}
			finally
			{
				_gate.Release();
			}
		}

		private async Task CompactAsync(CancellationToken ct)
		{
			await FlushCoreAsync(ct);
			_data.SetLength(0);
			_data.Flush(flushToDisk: true);
			_committedOffset = 0;
			WriteOffset(0);
			_logger?.LogInformation("WAL dipangkas; seluruh isi sudah tersimpan di historian");
		}

		/// <summary>
		/// Menulis offset lewat berkas sementara + rename. Menimpa berkas offset di tempat
		/// berisiko: mati listrik di tengah penulisan bisa meninggalkan offset separuh tertulis,
		/// dan offset yang rusak jauh lebih berbahaya daripada mengulang satu batch.
		/// </summary>
		private void WriteOffset(long offset)
		{
			var temp = _offsetPath + ".tmp";
			var bytes = new byte[sizeof(long)];
			BinaryPrimitives.WriteInt64LittleEndian(bytes, offset);

			using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				stream.Write(bytes);
				stream.Flush(flushToDisk: true);
			}

			File.Move(temp, _offsetPath, overwrite: true);
		}

		private long ReadOffset()
		{
			try
			{
				if (!File.Exists(_offsetPath)) return 0;
				var bytes = File.ReadAllBytes(_offsetPath);
				return bytes.Length == sizeof(long) ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : 0;
			}
			catch (IOException ex)
			{
				_logger?.LogWarning(ex, "Berkas offset WAL tidak terbaca; mulai dari awal");
				return 0;
			}
		}

		public BufferStats GetStats() => new()
		{
			PendingBytes = Math.Max(0, _data.Length - _committedOffset),
			TotalBytes = _data.Length,
			AppendedCount = _appended,
			CommittedCount = _committed
		};

		/* ------------------------------------------------------------------ format */

		private static byte[] Encode(TagSample sample)
		{
			var textBytes = sample.Text is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(sample.Text);

			byte flags = 0;
			if (sample.Numeric.HasValue) flags |= FlagNumeric;
			if (sample.Boolean.HasValue) flags |= FlagBoolean;
			if (textBytes.Length > 0) flags |= FlagText;
			if (sample.Raw.HasValue) flags |= FlagRaw;

			var size = 16 + 16 + 1 + 1 + 8 + 8            // id, id, flags, quality, 2 timestamp
					   + (sample.Numeric.HasValue ? 8 : 0)
					   + (sample.Boolean.HasValue ? 1 : 0)
					   + (sample.Raw.HasValue ? 8 : 0)
					   + (textBytes.Length > 0 ? 2 + textBytes.Length : 0);

			var buffer = new byte[size];
			var span = buffer.AsSpan();
			var offset = 0;

			sample.TagId.TryWriteBytes(span[offset..]); offset += 16;
			sample.DeviceId.TryWriteBytes(span[offset..]); offset += 16;
			span[offset++] = flags;
			span[offset++] = (byte)sample.Quality;

			BinaryPrimitives.WriteInt64LittleEndian(span[offset..], sample.SourceTs.Ticks); offset += 8;
			BinaryPrimitives.WriteInt64LittleEndian(span[offset..], sample.GatewayTs.Ticks); offset += 8;

			if (sample.Numeric.HasValue)
			{
				BinaryPrimitives.WriteDoubleLittleEndian(span[offset..], sample.Numeric.Value);
				offset += 8;
			}

			if (sample.Boolean.HasValue)
			{
				span[offset++] = sample.Boolean.Value ? (byte)1 : (byte)0;
			}

			if (sample.Raw.HasValue)
			{
				BinaryPrimitives.WriteDoubleLittleEndian(span[offset..], sample.Raw.Value);
				offset += 8;
			}

			if (textBytes.Length > 0)
			{
				BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)textBytes.Length);
				offset += 2;
				textBytes.CopyTo(span[offset..]);
			}

			return buffer;
		}

		private static TagSample Decode(ReadOnlySpan<byte> payload)
		{
			var offset = 0;

			var tagId = new Guid(payload.Slice(offset, 16)); offset += 16;
			var deviceId = new Guid(payload.Slice(offset, 16)); offset += 16;
			var flags = payload[offset++];
			var quality = (Quality)payload[offset++];

			var sourceTicks = BinaryPrimitives.ReadInt64LittleEndian(payload[offset..]); offset += 8;
			var gatewayTicks = BinaryPrimitives.ReadInt64LittleEndian(payload[offset..]); offset += 8;

			double? numeric = null;
			if ((flags & FlagNumeric) != 0)
			{
				numeric = BinaryPrimitives.ReadDoubleLittleEndian(payload[offset..]);
				offset += 8;
			}

			bool? boolean = null;
			if ((flags & FlagBoolean) != 0)
			{
				boolean = payload[offset++] == 1;
			}

			double? raw = null;
			if ((flags & FlagRaw) != 0)
			{
				raw = BinaryPrimitives.ReadDoubleLittleEndian(payload[offset..]);
				offset += 8;
			}

			string? text = null;
			if ((flags & FlagText) != 0)
			{
				var length = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
				offset += 2;
				text = Encoding.UTF8.GetString(payload.Slice(offset, length));
			}

			return new TagSample
			{
				TagId = tagId,
				DeviceId = deviceId,
				Numeric = numeric,
				Boolean = boolean,
				Text = text,
				Raw = raw,
				SourceTs = new DateTime(sourceTicks, DateTimeKind.Utc),
				GatewayTs = new DateTime(gatewayTicks, DateTimeKind.Utc),
				Quality = quality
			};
		}

		public async ValueTask DisposeAsync()
		{
			await FlushAsync();
			await _data.DisposeAsync();
			_gate.Dispose();
		}
	}
}
