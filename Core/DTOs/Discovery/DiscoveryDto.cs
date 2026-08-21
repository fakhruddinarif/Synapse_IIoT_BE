using System.ComponentModel.DataAnnotations;

namespace Core.DTOs.Discovery
{
	/// <summary>
	/// Satu key hasil pendataran payload — kandidat untuk menjadi satu tag.
	///
	/// Inilah yang membuat pemilih key di UI mungkin: pengguna memilih dari daftar
	/// yang benar-benar dikirim perangkat, bukan mengetik JSONPath dari ingatan.
	/// </summary>
	public class DiscoveredKeyDto
	{
		/// <summary>JSONPath yang nantinya menjadi <c>Tag.Address</c>, mis. "$.data.temperature".</summary>
		public string Path { get; set; } = string.Empty;

		/// <summary>Nama key terakhir saja, untuk ditampilkan di pohon: "temperature".</summary>
		public string Leaf { get; set; } = string.Empty;

		/// <summary>Kedalaman dari akar; dipakai UI untuk indentasi pohon tanpa mengurai path.</summary>
		public int Depth { get; set; }

		/// <summary>Tipe hasil penyimpulan, memakai nama <c>Core.Enums.DataType</c>.</summary>
		public string DataType { get; set; } = "STRING";

		/// <summary>Contoh nilai apa adanya. Yang membuat pengguna yakin memilih key yang benar.</summary>
		public object? SampleValue { get; set; }

		/// <summary>Saran nama tag, mis. "Oven_Temp". Boleh diubah pengguna.</summary>
		public string SuggestedTagName { get; set; } = string.Empty;

		/// <summary>Saran satuan dari nama key. SARAN, bukan keputusan — UI menandainya jelas.</summary>
		public string? SuggestedUnit { get; set; }

		/// <summary>Bisa digambar di grafik. Teks murni tidak bisa, tapi tetap boleh disimpan.</summary>
		public bool IsNumeric { get; set; }

		/// <summary>Key ini berada di dalam array; UI menawarkan penanganan eksplisit.</summary>
		public bool IsInArray { get; set; }

		/// <summary>Panjang array induk saat diprobe, supaya UI bisa menawarkan "buat N tag".</summary>
		public int? ArrayLength { get; set; }

		/// <summary>Catatan untuk pengguna, mis. tipe belum bisa disimpulkan karena nilainya null.</summary>
		public string? Note { get; set; }
	}

	/* ---------------------------------------------------------------- HTTP -- */

	public class HttpProbeRequestDto
	{
		[Required]
		[MaxLength(2000)]
		public string Url { get; set; } = string.Empty;

		[MaxLength(10)]
		public string Method { get; set; } = "GET";

		/// <summary>Header opsional; dikirim apa adanya ke endpoint.</summary>
		public Dictionary<string, string>? Headers { get; set; }

		/// <summary>Body untuk POST.</summary>
		public string? Body { get; set; }

		[Range(500, 30000)]
		public int TimeoutMs { get; set; } = 5000;
	}

	public class HttpProbeResultDto
	{
		public bool IsSuccess { get; set; }
		public int StatusCode { get; set; }
		public long LatencyMs { get; set; }
		public string? ContentType { get; set; }
		public int PayloadBytes { get; set; }

		/// <summary>Payload mentah yang sudah dirapikan, untuk diperlihatkan di UI.</summary>
		public string? RawPayload { get; set; }

		/// <summary>Payload bukan JSON — daftar key hanya memuat satu entri nilai mentah.</summary>
		public bool IsJson { get; set; }

		public List<DiscoveredKeyDto> Keys { get; set; } = new();
		public string? ErrorMessage { get; set; }
	}

	/* ---------------------------------------------------------------- MQTT -- */

	public class MqttSniffRequestDto
	{
		[Required]
		[MaxLength(255)]
		public string BrokerUrl { get; set; } = string.Empty;

		[Range(1, 65535)]
		public int Port { get; set; } = 1883;

		/// <summary>
		/// Dikosongkan berarti dibuatkan ClientId sementara khusus untuk pendengaran ini.
		/// Sengaja TIDAK memakai ClientId perangkat: memakai id yang sama dengan koneksi
		/// akuisisi akan membuat broker memutus koneksi yang sedang bekerja.
		/// </summary>
		[MaxLength(100)]
		public string? ClientId { get; set; }

		[Required]
		[MaxLength(500)]
		public string TopicFilter { get; set; } = "#";

		[MaxLength(100)]
		public string? Username { get; set; }

		[MaxLength(200)]
		public string? Password { get; set; }

		public bool UseTls { get; set; }

		/// <summary>
		/// Lama mendengarkan. Perangkat yang mengirim tiap 5 menit tidak akan muncul dalam
		/// 10 detik — UI menyatakan ini dan menawarkan sampai 60 detik.
		/// </summary>
		[Range(3, 60)]
		public int DurationSeconds { get; set; } = 10;
	}

	public class MqttTopicSampleDto
	{
		public string Topic { get; set; } = string.Empty;
		public int MessageCount { get; set; }

		/// <summary>Pesan per detik selama pendengaran — memberi tahu topik mana yang aktif.</summary>
		public double RatePerSecond { get; set; }

		/// <summary>Pesan retained: nilainya bisa lama, penting diketahui sebelum dipakai realtime.</summary>
		public bool IsRetained { get; set; }

		/// <summary>"json" | "number" | "text" | "empty" | "binary".</summary>
		public string PayloadKind { get; set; } = "text";

		public string? LastPayload { get; set; }
		public List<DiscoveredKeyDto> Keys { get; set; } = new();
	}

	public class MqttSniffResultDto
	{
		public bool IsSuccess { get; set; }
		public int DurationSeconds { get; set; }
		public int TotalMessages { get; set; }
		public List<MqttTopicSampleDto> Topics { get; set; } = new();
		public string? ErrorMessage { get; set; }

		/// <summary>
		/// Tersambung tapi tidak menerima satu pesan pun. Dibedakan dari gagal sambung karena
		/// tindak lanjutnya berbeda: perpanjang durasi / periksa filter topik, bukan periksa
		/// kredensial.
		/// </summary>
		public bool ConnectedButSilent { get; set; }
	}
}
