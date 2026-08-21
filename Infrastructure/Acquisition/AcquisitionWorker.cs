using System.Collections.Concurrent;
using System.Threading.Channels;
using Core.Acquisition;
using Core.Enums;
using Core.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Acquisition
{
	/// <summary>
	/// Penjadwal akuisisi: satu-satunya komponen yang memutuskan KAPAN perangkat dibaca.
	///
	/// BENTUK PENJADWALAN — satu timer per (perangkat, kelas scan), bukan per tag:
	///
	/// Pada 2.000 tag, satu timer per tag berarti 2.000 timer dan 2.000 permintaan protokol per
	/// putaran; jaringan dan PLC akan tumbang jauh sebelum CPU. Menariknya per kelas scan
	/// membuat 2.000 tag @ 1 s menjadi belasan permintaan kolektif per detik, sementara tag yang
	/// cukup dibaca 5 detik sekali tidak ikut dipaksa cepat.
	///
	/// TARIK vs DORONG:
	///
	/// Protokol tarik (HTTP, Modbus) dijalankan oleh tick timer. Protokol dorong (MQTT, OPC UA)
	/// tidak dijadwalkan sama sekali — nilai datang saat perangkat mengirimnya, dan memolesnya
	/// ulang dengan tick hanya akan menghasilkan sampel kembar ber-<c>sourceTs</c> sama. Untuk
	/// protokol dorong penjadwal hanya menjalankan pengawas: mendeteksi koneksi yang terputus,
	/// karena broker yang diam terlihat persis sama dengan pabrik yang sedang tenang.
	///
	/// HOT RELOAD:
	///
	/// Perubahan konfigurasi tidak pernah me-restart proses dan sejauh mungkin tidak me-restart
	/// koneksi. Menambah tag pada perangkat MQTT hanya menukar daftar tag dan memperbarui
	/// langganan; sesi broker, dan dengan itu pesan yang tertahan di dalamnya, tetap utuh.
	/// Koneksi hanya dibangun ulang bila alamat atau protokolnya sendiri yang berubah.
	///
	/// ISOLASI KEGAGALAN:
	///
	/// Setiap perangkat punya loop dan penanganan galatnya sendiri. Satu PLC yang kabelnya
	/// dicabut tidak boleh menghentikan sembilan belas lainnya — itu perilaku yang membuat
	/// gateway lebih berbahaya daripada tidak ada gateway.
	/// </summary>
	public sealed class AcquisitionWorker : BackgroundService, IAcquisitionControl
	{
		private sealed class DeviceRuntime
		{
			public required DevicePlan Plan { get; set; }
			public required IDeviceDriver Driver { get; init; }
			public required CancellationTokenSource DeviceCts { get; init; }

			/// <summary>CTS terpisah untuk loop scan, supaya kelas scan bisa disusun ulang tanpa
			/// menyentuh koneksi driver.</summary>
			public CancellationTokenSource? LoopCts { get; set; }
			public List<Task> Loops { get; } = [];

			public int ConsecutiveFailures;
			public DateTime? NextAttemptUtc;
			public DateTime? GapSince;
			public string? LastError;
			public bool IsConnected;
			public IReadOnlyList<int> ScanClasses { get; set; } = [];
		}

		private readonly IAcquisitionPlanSource _planSource;
		private readonly IDeviceDriverFactory _driverFactory;
		private readonly ITagEngine _engine;
		private readonly ISampleBuffer _buffer;
		private readonly IGapLedger _gaps;
		private readonly RealtimeCoalescer _realtime;
		private readonly AcquisitionOptions _options;
		private readonly ILogger<AcquisitionWorker> _logger;

		private readonly ConcurrentDictionary<Guid, DeviceRuntime> _runtimes = new();
		private readonly Channel<string> _replanSignals;
		private readonly Channel<TagSample> _storeQueue;

		private long _replanCount;
		private long _samplesAcquired;
		private long _samplesStored;
		private DateTime? _lastReplanAt;
		private string? _lastReplanReason;
		private volatile bool _isRunning;

		public AcquisitionWorker(
			IAcquisitionPlanSource planSource,
			IDeviceDriverFactory driverFactory,
			ITagEngine engine,
			ISampleBuffer buffer,
			IGapLedger gaps,
			RealtimeCoalescer realtime,
			AcquisitionOptions options,
			ILogger<AcquisitionWorker> logger)
		{
			_planSource = planSource;
			_driverFactory = driverFactory;
			_engine = engine;
			_buffer = buffer;
			_gaps = gaps;
			_realtime = realtime;
			_options = options;
			_logger = logger;

			// DropWrite pada sinyal replan aman: sinyal hanyalah "ada yang berubah", tanpa isi.
			// Kehilangan sinyal kedua saat satu sudah menunggu tidak mengubah hasil apa pun.
			_replanSignals = Channel.CreateBounded<string>(
				new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropWrite });

			// Antrean sampel justru TIDAK boleh membuang: setiap elemen di sini adalah nilai yang
			// sudah berhasil dibaca dari perangkat, dan perangkat polling tidak bisa mengulanginya.
			// Penuh berarti akuisisi menunggu, bukan sampel dibuang.
			_storeQueue = Channel.CreateBounded<TagSample>(
				new BoundedChannelOptions(options.StoreQueueCapacity) { FullMode = BoundedChannelFullMode.Wait });
		}

		/* =========================== siklus hidup =========================== */

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_isRunning = true;
			_logger.LogInformation("Worker akuisisi mulai");

			var storeLoop = Task.Run(() => StoreLoopAsync(stoppingToken), CancellationToken.None);

			// Muat rencana pertama. Kegagalan di sini (umumnya database belum siap) tidak boleh
			// mematikan worker — ia harus bisa dicoba lagi, bukan menyerah untuk selamanya.
			await SafeApplyAsync("startup", stoppingToken);

			try
			{
				await ReplanLoopAsync(stoppingToken);
			}
			catch (OperationCanceledException)
			{
				// Penghentian normal.
			}
			finally
			{
				_isRunning = false;
				await StopAllDevicesAsync();
				_storeQueue.Writer.TryComplete();

				try
				{
					await storeLoop;
				}
				catch (OperationCanceledException)
				{
					// Penghentian normal.
				}

				// Sampel yang masih tertahan di buffer OS harus turun ke disk sebelum proses
				// hilang — inilah bedanya "shutdown bersih" dengan "kehilangan detik terakhir".
				await _buffer.FlushAsync(CancellationToken.None);
				_logger.LogInformation("Worker akuisisi berhenti");
			}
		}

		public void RequestReload(string reason) => _replanSignals.Writer.TryWrite(reason);

		/* ====================== replan dengan debounce ====================== */

		private async Task ReplanLoopAsync(CancellationToken ct)
		{
			while (!ct.IsCancellationRequested)
			{
				var reason = await _replanSignals.Reader.ReadAsync(ct);
				var coalesced = 1;

				// Menunggu sampai sinyal berhenti mengalir selama satu jendela debounce penuh.
				// Membuat 200 tag lewat endpoint massal memicu 200 sinyal; yang dijalankan tetap
				// satu penyusunan ulang, dan itu terjadi setelah tag terakhir tersimpan.
				while (await HasMoreSignalsAsync(_options.ReplanDebounceMs, ct))
				{
					while (_replanSignals.Reader.TryRead(out _)) coalesced++;
				}

				if (coalesced > 1)
					_logger.LogInformation("Replan: {Count} perubahan digabung menjadi satu ({Reason})", coalesced, reason);

				await SafeApplyAsync(reason, ct);
			}
		}

		private async Task<bool> HasMoreSignalsAsync(int windowMs, CancellationToken ct)
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(windowMs);

			try
			{
				return await _replanSignals.Reader.WaitToReadAsync(timeout.Token);
			}
			catch (OperationCanceledException) when (!ct.IsCancellationRequested)
			{
				return false; // jendela habis tanpa sinyal baru — inilah jalur normalnya
			}
		}

		private async Task SafeApplyAsync(string reason, CancellationToken ct)
		{
			try
			{
				await ApplyPlansAsync(reason, ct);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				// Rencana yang gagal dimuat menyisakan rencana LAMA tetap berjalan. Itu jauh lebih
				// baik daripada menghentikan akuisisi karena satu kueri gagal.
				_logger.LogError(ex, "Gagal menyusun ulang rencana akuisisi ({Reason}); rencana sebelumnya tetap jalan", reason);
			}
		}

		private async Task ApplyPlansAsync(string reason, CancellationToken ct)
		{
			var plans = await _planSource.GetActivePlansAsync(ct);

			// 1. Perangkat yang hilang atau dimatikan.
			var activeIds = plans.Select(p => p.DeviceId).ToHashSet();
			foreach (var (deviceId, runtime) in _runtimes.ToArray())
			{
				if (activeIds.Contains(deviceId)) continue;

				_logger.LogInformation("Perangkat {Device} tidak lagi aktif; menghentikan akuisisi", runtime.Plan.DeviceName);
				await StopDeviceAsync(runtime);
				_runtimes.TryRemove(deviceId, out _);
				_engine.Forget(runtime.Plan.Tags.Select(t => t.TagId));
			}

			// 2. Perangkat baru dan yang berubah.
			foreach (var plan in plans)
			{
				if (!_runtimes.TryGetValue(plan.DeviceId, out var runtime))
				{
					StartDevice(plan);
					continue;
				}

				if (NeedsReconnect(runtime.Plan, plan))
				{
					_logger.LogInformation("Koneksi perangkat {Device} berubah; menyambung ulang", plan.DeviceName);
					await StopDeviceAsync(runtime);
					_runtimes.TryRemove(plan.DeviceId, out _);
					StartDevice(plan);
					continue;
				}

				if (!SameTags(runtime.Plan, plan))
				{
					var removed = runtime.Plan.Tags.Select(t => t.TagId)
						.Except(plan.Tags.Select(t => t.TagId)).ToList();

					_logger.LogInformation(
						"Tag perangkat {Device} berubah ({Before} → {After}); menukar rencana tanpa menyambung ulang",
						plan.DeviceName, runtime.Plan.Tags.Count, plan.Tags.Count);

					runtime.Plan = plan;
					if (removed.Count > 0) _engine.Forget(removed);
					await RestartLoopsAsync(runtime);
				}
			}

			Interlocked.Increment(ref _replanCount);
			_lastReplanAt = DateTime.UtcNow;
			_lastReplanReason = reason;
		}

		/// <summary>
		/// Hanya alamat dan protokol yang memaksa koneksi baru. Perbedaannya penting: menyambung
		/// ulang klien MQTT berarti kehilangan sesi persisten, dan dengan itu pesan yang ditahan
		/// broker selama gateway sibuk.
		/// </summary>
		private static bool NeedsReconnect(DevicePlan before, DevicePlan after) =>
			before.Protocol != after.Protocol ||
			!string.Equals(before.ConnectionConfigJson, after.ConnectionConfigJson, StringComparison.Ordinal);

		private static bool SameTags(DevicePlan before, DevicePlan after)
		{
			if (before.Tags.Count != after.Tags.Count) return false;
			if (before.ScanIntervalMs != after.ScanIntervalMs) return false;

			// TagPlan adalah record berisi skalar saja, jadi kesetaraan nilainya menangkap semua
			// perubahan yang penting: alamat, penskalaan, deadband, kelas scan.
			var a = before.Tags.OrderBy(t => t.TagId).ToList();
			var b = after.Tags.OrderBy(t => t.TagId).ToList();
			return a.SequenceEqual(b);
		}

		/* ======================== per perangkat ======================== */

		private void StartDevice(DevicePlan plan)
		{
			IDeviceDriver driver;
			try
			{
				driver = _driverFactory.Create(plan);
			}
			catch (NotSupportedException ex)
			{
				// Protokol tanpa driver dilewati dengan jelas, bukan dianggap sehat. Perangkat
				// yang tampak "hijau" tetapi tidak pernah dibaca adalah kegagalan paling
				// berbahaya di sistem seperti ini.
				_logger.LogWarning("Perangkat {Device} dilewati: {Reason}", plan.DeviceName, ex.Message);
				return;
			}

			var runtime = new DeviceRuntime
			{
				Plan = plan,
				Driver = driver,
				DeviceCts = new CancellationTokenSource()
			};

			if (!_runtimes.TryAdd(plan.DeviceId, runtime))
			{
				_ = driver.DisposeAsync();
				return;
			}

			_ = Task.Run(() => RunDeviceAsync(runtime), CancellationToken.None);
			_logger.LogInformation(
				"Perangkat {Device} ({Protocol}) mulai diakuisisi, {Tags} tag",
				plan.DeviceName, plan.Protocol, plan.Tags.Count);
		}

		private async Task RunDeviceAsync(DeviceRuntime runtime)
		{
			var ct = runtime.DeviceCts.Token;

			while (!ct.IsCancellationRequested)
			{
				try
				{
					await runtime.Driver.ConnectAsync(ct);
					runtime.IsConnected = true;
					runtime.LastError = null;
					OnDeviceSuccess(runtime);

					await RestartLoopsAsync(runtime);

					// Loop scan (atau pengawas) berjalan sampai perangkat dihentikan; loop yang
					// disusun ulang saat hot reload diganti di dalam RestartLoopsAsync, jadi
					// menunggu di sini tidak boleh memakai daftar yang sudah usang.
					await WaitForLoopsAsync(runtime, ct);
					if (ct.IsCancellationRequested) return;

					// Loop berakhir sendiri. Untuk protokol dorong itu berarti pengawas melihat
					// koneksi gugur — klien MQTT biasa tidak menyambung ulang sendiri, jadi
					// penyambungan ulang memang tugas siklus ini.
					runtime.IsConnected = false;
					OnDeviceFailure(runtime, runtime.LastError ?? "loop akuisisi berakhir");

					var retryIn = BackoffFor(Volatile.Read(ref runtime.ConsecutiveFailures));
					_logger.LogWarning(
						"Akuisisi perangkat {Device} berhenti ({Error}); menyambung ulang dalam {Delay}s",
						runtime.Plan.DeviceName, runtime.LastError, retryIn.TotalSeconds);

					await Task.Delay(retryIn, ct);
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					runtime.IsConnected = false;
					OnDeviceFailure(runtime, ex.Message);

					var delay = BackoffFor(Volatile.Read(ref runtime.ConsecutiveFailures));
					_logger.LogWarning(
						"Perangkat {Device} gagal ({Failures}×): {Error}. Coba lagi dalam {Delay}s",
						runtime.Plan.DeviceName, Volatile.Read(ref runtime.ConsecutiveFailures),
						ex.Message, delay.TotalSeconds);

					try
					{
						await Task.Delay(delay, ct);
					}
					catch (OperationCanceledException)
					{
						return;
					}
				}
			}
		}

		/// <summary>
		/// Menunggu loop perangkat selesai. Karena hot reload menukar daftar loop di tengah
		/// jalan, penungguan diperiksa berkala alih-alih sekali <c>WhenAll</c> atas daftar yang
		/// bisa berubah — <c>WhenAll</c> atas daftar lama akan kembali tepat setelah replan dan
		/// memicu penyambungan ulang yang tidak diminta.
		/// </summary>
		private async Task WaitForLoopsAsync(DeviceRuntime runtime, CancellationToken ct)
		{
			while (!ct.IsCancellationRequested)
			{
				Task[] snapshot;
				lock (runtime.Loops)
				{
					snapshot = runtime.Loops.ToArray();
				}

				if (snapshot.Length == 0)
				{
					await Task.Delay(250, ct);
					continue;
				}

				var all = Task.WhenAll(snapshot);
				var finished = await Task.WhenAny(all, Task.Delay(500, ct));

				// Hanya berarti "akuisisi berakhir" bila loop yang DITUNGGU-lah yang selesai;
				// habisnya jendela 500 ms cuma tanda untuk memeriksa ulang daftar loop, karena
				// hot reload bisa saja sudah menggantinya.
				if (finished == all) return;
			}
		}

		/// <summary>
		/// Menyusun ulang loop scan tanpa menyentuh koneksi. Dipanggil saat mulai dan setiap kali
		/// daftar tag atau kelas scannya berubah.
		/// </summary>
		private async Task RestartLoopsAsync(DeviceRuntime runtime)
		{
			var previousCts = runtime.LoopCts;
			Task[] previousLoops;
			lock (runtime.Loops)
			{
				previousLoops = runtime.Loops.ToArray();
				runtime.Loops.Clear();
			}

			if (previousCts is not null)
			{
				await previousCts.CancelAsync();
				try
				{
					await Task.WhenAll(previousLoops);
				}
				catch (Exception)
				{
					// Loop yang dibatalkan atau gagal sudah dicatat di dalam loop itu sendiri.
				}
				previousCts.Dispose();
			}

			runtime.LoopCts = CancellationTokenSource.CreateLinkedTokenSource(runtime.DeviceCts.Token);
			var loopCt = runtime.LoopCts.Token;
			var plan = runtime.Plan;

			if (IsPushProtocol(plan.Protocol))
			{
				// Protokol dorong: langganan mengikuti daftar tag baru, lalu pengawas berjalan.
				// Tidak ada tick pembacaan — nilai datang sendiri dari broker.
				await runtime.Driver.SubscribeAsync(
					plan.Tags,
					(sample, token) => HandleSampleAsync(runtime, sample, token),
					loopCt);

				runtime.ScanClasses = [_options.PushWatchdogMs];
				lock (runtime.Loops)
				{
					runtime.Loops.Add(Task.Run(() => WatchdogLoopAsync(runtime, loopCt), CancellationToken.None));
				}
				return;
			}

			var groups = plan.Tags
				.GroupBy(t => Math.Max(_options.MinScanIntervalMs, t.ScanIntervalMs ?? plan.ScanIntervalMs))
				.OrderBy(g => g.Key)
				.ToList();

			runtime.ScanClasses = groups.Select(g => g.Key).ToList();

			foreach (var group in groups)
			{
				var intervalMs = group.Key;
				var tags = group.ToList();
				lock (runtime.Loops)
				{
					runtime.Loops.Add(Task.Run(
						() => ScanLoopAsync(runtime, intervalMs, tags, loopCt), CancellationToken.None));
				}
			}

			if (groups.Count > 1)
			{
				_logger.LogInformation(
					"Perangkat {Device} memakai {Count} kelas scan: {Classes} ms",
					plan.DeviceName, groups.Count, string.Join(", ", runtime.ScanClasses));
			}
		}

		private async Task ScanLoopAsync(DeviceRuntime runtime, int intervalMs, List<TagPlan> tags, CancellationToken ct)
		{
			// PeriodicTimer, bukan Task.Delay setelah pekerjaan: yang kedua membuat periode
			// sebenarnya menjadi interval + durasi pembacaan, sehingga scan 1 s pada perangkat
			// berlatensi 300 ms sesungguhnya berjalan 1,3 s — dan grafik antar perangkat tidak
			// lagi sejajar.
			using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));

			while (!ct.IsCancellationRequested)
			{
				try
				{
					if (!await timer.WaitForNextTickAsync(ct)) return;
				}
				catch (OperationCanceledException)
				{
					return;
				}

				// Backoff: perangkat yang sedang gagal tidak dibaca setiap tick.
				if (runtime.NextAttemptUtc is { } next && DateTime.UtcNow < next) continue;

				try
				{
					var samples = await runtime.Driver.ReadAsync(tags, ct);
					var allBad = samples.Count > 0 && samples.All(s => s.Quality == Quality.Bad);

					foreach (var sample in samples)
					{
						await HandleSampleAsync(runtime, sample, ct);
					}

					if (allBad)
					{
						runtime.IsConnected = false;
						OnDeviceFailure(runtime, samples[0].Note ?? "semua tag Bad");
					}
					else
					{
						runtime.IsConnected = true;
						OnDeviceSuccess(runtime);
					}
				}
				catch (OperationCanceledException)
				{
					return;
				}
				catch (Exception ex)
				{
					// Satu tick yang gagal tidak menghentikan loop, dan tidak menyentuh perangkat
					// lain. Tagnya ditandai Bad supaya dasbor tidak memamerkan nilai basi seolah
					// masih hidup.
					_engine.MarkDeviceBad(runtime.Plan.DeviceId, tags, ex.Message);
					OnDeviceFailure(runtime, ex.Message);
					_logger.LogWarning(ex, "Tick scan {Interval} ms perangkat {Device} gagal",
						intervalMs, runtime.Plan.DeviceName);
				}
			}
		}

		/// <summary>
		/// Pengawas protokol dorong. Broker yang diam dan pabrik yang tenang terlihat identik
		/// dari luar; satu-satunya cara membedakannya adalah memeriksa keadaan koneksi, dan
		/// membiarkan tag menjadi Stale sendiri lewat <c>StaleAfterMs</c>.
		/// </summary>
		private async Task WatchdogLoopAsync(DeviceRuntime runtime, CancellationToken ct)
		{
			using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PushWatchdogMs));

			while (!ct.IsCancellationRequested)
			{
				try
				{
					if (!await timer.WaitForNextTickAsync(ct)) return;
				}
				catch (OperationCanceledException)
				{
					return;
				}

				var health = runtime.Driver.Health;
				if (health.IsConnected)
				{
					runtime.IsConnected = true;
					OnDeviceSuccess(runtime);
					continue;
				}

				runtime.IsConnected = false;
				var reason = health.LastError ?? "koneksi terputus";
				_engine.MarkDeviceBad(runtime.Plan.DeviceId, runtime.Plan.Tags, reason);
				runtime.LastError = reason;

				// Keluar, bukan menunggu di tempat: klien MQTT biasa tidak menyambung ulang
				// sendiri, jadi berakhirnya pengawas inilah yang memberi tahu siklus perangkat
				// bahwa koneksi harus dibangun ulang. Pengawas yang terus berputar sambil
				// melaporkan "terputus" akan membuat perangkat mati selamanya tanpa ada yang
				// mencoba menyambungkannya.
				return;
			}
		}

		/// <summary>
		/// Jalur panas: satu sampel mentah dari driver sampai ke buffer dan ke klien.
		/// Urutannya disengaja — realtime lebih dulu supaya tampilan tidak menunggu disk.
		/// </summary>
		private async Task HandleSampleAsync(DeviceRuntime runtime, TagSample raw, CancellationToken ct)
		{
			var tag = runtime.Plan.Tags.FirstOrDefault(t => t.TagId == raw.TagId);
			if (tag is null) return; // tag dihapus di tengah tick; sampelnya tidak lagi punya rencana

			var (sample, shouldStore, _) = _engine.Process(tag, raw);
			Interlocked.Increment(ref _samplesAcquired);

			_realtime.Enqueue(runtime.Plan.DeviceId, runtime.Plan.DeviceName, sample);

			if (!shouldStore) return;

			// WriteAsync menunggu bila antrean penuh — akuisisi ikut melambat, dan itu memang
			// yang diinginkan: melambat bisa dipulihkan, sampel yang dibuang tidak.
			await _storeQueue.Writer.WriteAsync(sample, ct);
		}

		private void OnDeviceSuccess(DeviceRuntime runtime)
		{
			runtime.NextAttemptUtc = null;
			Interlocked.Exchange(ref runtime.ConsecutiveFailures, 0);

			if (runtime.GapSince is not { } since) return;

			runtime.GapSince = null;
			_ = SafeGapAsync(() => _gaps.CloseAsync(runtime.Plan.DeviceId, DateTime.UtcNow, CancellationToken.None));
			_logger.LogInformation(
				"Perangkat {Device} kembali normal; jeda akuisisi sejak {Since:O} ditutup",
				runtime.Plan.DeviceName, since);
		}

		private void OnDeviceFailure(DeviceRuntime runtime, string reason)
		{
			var failures = Interlocked.Increment(ref runtime.ConsecutiveFailures);
			runtime.LastError = reason;
			runtime.NextAttemptUtc = DateTime.UtcNow.Add(BackoffFor(failures));

			if (failures < _options.FailuresBeforeGap || runtime.GapSince is not null) return;

			// Jeda dicatat mundur ke perkiraan kegagalan pertama, bukan ke saat ambang tercapai —
			// kalau tidak, catatan akan menyatakan data ada padahal tidak.
			var from = DateTime.UtcNow.Add(-BackoffFor(failures - 1));
			runtime.GapSince = from;
			_ = SafeGapAsync(() => _gaps.OpenAsync(
				runtime.Plan.DeviceId, runtime.Plan.DeviceName, from, reason, CancellationToken.None));

			_logger.LogWarning(
				"Jeda akuisisi perangkat {Device} dicatat sejak {From:O}: {Reason}",
				runtime.Plan.DeviceName, from, reason);
		}

		private async Task SafeGapAsync(Func<Task> action)
		{
			try
			{
				await action();
			}
			catch (Exception ex)
			{
				// Catatan jeda yang gagal disimpan tidak boleh menjatuhkan akuisisi; jedanya
				// sendiri sudah terlihat di log dan di status perangkat.
				_logger.LogWarning(ex, "Gagal mencatat jeda akuisisi");
			}
		}

		private TimeSpan BackoffFor(int failures)
		{
			var ladder = _options.BackoffSeconds;
			if (ladder.Length == 0) return TimeSpan.FromSeconds(5);
			var index = Math.Clamp(failures - 1, 0, ladder.Length - 1);
			return TimeSpan.FromSeconds(ladder[index]);
		}

		private static bool IsPushProtocol(Protocol protocol) =>
			protocol is Protocol.MQTT or Protocol.OPC_UA;

		private async Task StopDeviceAsync(DeviceRuntime runtime)
		{
			await runtime.DeviceCts.CancelAsync();

			Task[] loops;
			lock (runtime.Loops)
			{
				loops = runtime.Loops.ToArray();
			}

			try
			{
				if (loops.Length > 0) await Task.WhenAll(loops);
			}
			catch (Exception)
			{
				// Loop yang dibatalkan; sudah dicatat di tempatnya.
			}

			try
			{
				await runtime.Driver.DisposeAsync();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Gagal menutup driver {Device}", runtime.Plan.DeviceName);
			}

			runtime.LoopCts?.Dispose();
			runtime.DeviceCts.Dispose();
			runtime.IsConnected = false;
		}

		private async Task StopAllDevicesAsync()
		{
			foreach (var (id, runtime) in _runtimes.ToArray())
			{
				await StopDeviceAsync(runtime);
				_runtimes.TryRemove(id, out _);
			}
		}

		/* ================== penulisan ke buffer tahan-mati ================== */

		private async Task StoreLoopAsync(CancellationToken ct)
		{
			var batch = new List<TagSample>(_options.StoreBatchSize);
			var reader = _storeQueue.Reader;

			while (!ct.IsCancellationRequested)
			{
				try
				{
					if (!await reader.WaitToReadAsync(ct)) break;

					// Kumpulkan sebanyak yang sudah tersedia tanpa menunggu: satu append berisi
					// 500 sampel jauh lebih murah daripada 500 append berisi satu.
					while (batch.Count < _options.StoreBatchSize && reader.TryRead(out var sample))
					{
						batch.Add(sample);
					}

					if (batch.Count == 0) continue;

					await _buffer.AppendAsync(batch, ct);
					Interlocked.Add(ref _samplesStored, batch.Count);
					batch.Clear();

					// Beri kesempatan sampel berikutnya berkumpul, tanpa menahan sampel terakhir
					// lebih lama dari jendela ini.
					if (reader.Count == 0) await Task.Delay(_options.StoreFlushIntervalMs, ct);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Gagal menulis {Count} sampel ke buffer", batch.Count);

					// Buffer yang gagal ditulis adalah keadaan serius: sampel di tangan tidak
					// punya tempat aman. Ditahan dan dicoba lagi, tidak dibuang.
					await Task.Delay(1_000, CancellationToken.None);
				}
			}

			// Kuras sisa antrean saat berhenti — sampel yang sudah diakuisisi tetap harus tersimpan.
			var tail = new List<TagSample>(batch);
			while (_storeQueue.Reader.TryRead(out var leftover)) tail.Add(leftover);

			if (tail.Count > 0)
			{
				try
				{
					await _buffer.AppendAsync(tail, CancellationToken.None);
					Interlocked.Add(ref _samplesStored, tail.Count);
					_logger.LogInformation("{Count} sampel sisa ditulis saat penghentian", tail.Count);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Gagal menulis {Count} sampel sisa saat penghentian", tail.Count);
				}
			}
		}

		/* ============================== status ============================== */

		public AcquisitionStatus GetStatus()
		{
			var devices = _runtimes.Values
				.Select(r => new DeviceRuntimeStatus
				{
					DeviceId = r.Plan.DeviceId,
					DeviceName = r.Plan.DeviceName,
					Protocol = r.Plan.Protocol,
					IsConnected = r.IsConnected,
					TagCount = r.Plan.Tags.Count,
					ScanClasses = r.ScanClasses,
					ConsecutiveFailures = Volatile.Read(ref r.ConsecutiveFailures),
					LastError = r.LastError,
					LastSuccessAt = r.Driver.Health.LastSuccessAt,
					GapSince = r.GapSince
				})
				.OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
				.ToList();

			return new AcquisitionStatus
			{
				IsRunning = _isRunning,
				DeviceCount = devices.Count,
				TagCount = devices.Sum(d => d.TagCount),
				ReplanCount = Interlocked.Read(ref _replanCount),
				LastReplanAt = _lastReplanAt,
				LastReplanReason = _lastReplanReason,
				SamplesAcquired = Interlocked.Read(ref _samplesAcquired),
				SamplesStored = Interlocked.Read(ref _samplesStored),
				BufferPendingBytes = _buffer.GetStats().PendingBytes,
				SupportedProtocols = _driverFactory.SupportedProtocols
					.Select(p => p.ToString()).OrderBy(p => p).ToList(),
				Devices = devices
			};
		}
	}
}
