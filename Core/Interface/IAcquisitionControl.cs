using Core.Enums;

namespace Core.Interface
{
	/// <summary>
	/// Kendali worker akuisisi dari luar — dipakai lapisan service setiap kali konfigurasi tag
	/// atau perangkat berubah.
	///
	/// Inilah yang membuat "tag baru langsung ditarik" bekerja tanpa restart: service tidak perlu
	/// tahu apa pun tentang penjadwalan, ia hanya memberi tahu bahwa rencana sudah berubah.
	/// </summary>
	public interface IAcquisitionControl
	{
		/// <summary>
		/// Meminta penyusunan ulang rencana. Aman dipanggil berkali-kali dan sangat cepat —
		/// permintaan digabung (debounce), jadi membuat 50 tag sekaligus menghasilkan satu
		/// penyusunan ulang, bukan lima puluh.
		/// </summary>
		void RequestReload(string reason);

		AcquisitionStatus GetStatus();
	}

	public sealed record AcquisitionStatus
	{
		public required bool IsRunning { get; init; }
		public required int DeviceCount { get; init; }
		public required int TagCount { get; init; }

		/// <summary>Berapa kali rencana disusun ulang sejak proses hidup. Jauh lebih kecil dari
		/// jumlah perubahan konfigurasi bila debounce bekerja.</summary>
		public required long ReplanCount { get; init; }

		public DateTime? LastReplanAt { get; init; }
		public string? LastReplanReason { get; init; }
		public required long SamplesAcquired { get; init; }
		public required long SamplesStored { get; init; }
		public required long BufferPendingBytes { get; init; }
		/// <summary>Protokol yang benar-benar punya driver. Dikirim ke klien supaya UI tidak
		/// menawarkan protokol yang akan dilewati penjadwal — perangkat yang tampak dibuat
		/// dengan benar tapi tidak pernah dibaca adalah kegagalan yang paling mahal di sini.</summary>
		public required IReadOnlyList<string> SupportedProtocols { get; init; }

		public required IReadOnlyList<DeviceRuntimeStatus> Devices { get; init; }
	}

	public sealed record DeviceRuntimeStatus
	{
		public required Guid DeviceId { get; init; }
		public required string DeviceName { get; init; }
		public required Protocol Protocol { get; init; }
		public required bool IsConnected { get; init; }
		public required int TagCount { get; init; }

		/// <summary>Kelas scan yang aktif untuk perangkat ini, dalam ms.</summary>
		public required IReadOnlyList<int> ScanClasses { get; init; }

		public required int ConsecutiveFailures { get; init; }
		public string? LastError { get; init; }
		public DateTime? LastSuccessAt { get; init; }

		/// <summary>Terisi bila perangkat sedang dalam jeda akuisisi yang tercatat.</summary>
		public DateTime? GapSince { get; init; }
	}
}
