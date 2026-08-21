namespace Infrastructure.Acquisition
{
	/// <summary>Setelan penjadwal akuisisi. Semua nilai punya baku yang aman untuk pabrik.</summary>
	public sealed class AcquisitionOptions
	{
		/// <summary>Jendela penggabungan permintaan replan. Membuat 200 tag lewat impor massal
		/// hanya memicu satu penyusunan ulang.</summary>
		public int ReplanDebounceMs { get; set; } = 500;

		/// <summary>Jendela penggabungan frame realtime.</summary>
		public int RealtimeWindowMs { get; set; } = 250;

		/// <summary>Batas bawah interval scan. Melindungi dari salah ketik "1" yang akan
		/// membanjiri PLC dengan seribu permintaan per detik.</summary>
		public int MinScanIntervalMs { get; set; } = 100;

		/// <summary>Jumlah kegagalan berurutan sebelum jeda akuisisi dicatat. Lebih dari satu,
		/// supaya satu paket hilang tidak memenuhi catatan dengan jeda semu.</summary>
		public int FailuresBeforeGap { get; set; } = 3;

		/// <summary>Tangga backoff koneksi dalam detik. Perangkat yang mati tidak boleh dicoba
		/// 500 ms sekali selama semalam — itu membanjiri log dan jaringan tanpa hasil.</summary>
		public int[] BackoffSeconds { get; set; } = [1, 2, 5, 15, 30, 60];

		/// <summary>Kapasitas antrean penyimpanan. Bila penuh, akuisisi ikut melambat
		/// (backpressure) — pilihan sadar: lebih baik lambat daripada membuang sampel.</summary>
		public int StoreQueueCapacity { get; set; } = 50_000;

		/// <summary>Ukuran batch maksimum saat menulis ke buffer tahan-mati.</summary>
		public int StoreBatchSize { get; set; } = 500;

		/// <summary>Jeda maksimum sebelum batch yang belum penuh tetap ditulis.</summary>
		public int StoreFlushIntervalMs { get; set; } = 200;

		/// <summary>Interval pengawas untuk protokol dorong (MQTT/OPC UA), yang tidak punya tick
		/// scan sendiri tetapi tetap harus mendeteksi koneksi yang diam.</summary>
		public int PushWatchdogMs { get; set; } = 2_000;
	}
}
