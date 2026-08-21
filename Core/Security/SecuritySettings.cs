namespace Core.Security
{
	/// <summary>
	/// Setelan keamanan yang dibaca dari konfigurasi dan <b>divalidasi saat startup</b>.
	///
	/// Kenapa divalidasi saat startup dan bukan saat dipakai: konfigurasi keamanan yang
	/// salah harus membuat aplikasi menolak menyala, bukan menyala dengan diam-diam lemah.
	/// Gateway yang berjalan dengan JWT secret bawaan terlihat sehat di semua dasbor, dan
	/// baru terlihat salah setelah ada yang memanfaatkannya.
	/// </summary>
	public class SecuritySettings
	{
		public const string SectionName = "Security";

		/// <summary>
		/// Origin yang boleh memanggil API (CORS) sekaligus daftar rujukan untuk validasi
		/// header <c>Origin</c> pada request yang mengubah data.
		/// </summary>
		public List<string> AllowedOrigins { get; set; } = new();

		/// <summary>
		/// Cookie sesi ditandai <c>Secure</c>. Wajib <c>true</c> di produksi; boleh
		/// <c>false</c> hanya untuk pengembangan di HTTP lokal.
		/// </summary>
		public bool CookieSecure { get; set; } = true;

		/// <summary>
		/// <c>Strict</c> membuat cookie tidak pernah dikirim pada navigasi lintas situs —
		/// pertahanan CSRF utama untuk autentikasi berbasis cookie. Dipakai bersama
		/// validasi <c>Origin</c>, bukan sebagai satu-satunya lapisan.
		/// </summary>
		public string CookieSameSite { get; set; } = "Strict";

		/// <summary>Percobaan login gagal sebelum akun dikunci sementara.</summary>
		public int MaxLoginAttempts { get; set; } = 5;

		/// <summary>Jendela penghitungan percobaan gagal (menit).</summary>
		public int LoginAttemptWindowMinutes { get; set; } = 15;

		/// <summary>Lama penguncian setelah ambang terlampaui (menit).</summary>
		public int LockoutMinutes { get; set; } = 15;

		/// <summary>
		/// Menyertakan tipe dan pesan exception pada respons 500. HANYA untuk pengembangan;
		/// di produksi klien menerima pesan umum + <c>traceId</c> untuk dicocokkan dengan log.
		/// </summary>
		public bool ExposeExceptionDetails { get; set; }

		/// <summary>
		/// Mengizinkan discovery memprobe alamat link-local (169.254.0.0/16), termasuk
		/// endpoint metadata cloud. Default <c>false</c>: perangkat lapangan tidak pernah
		/// berada di sana, sementara endpoint metadata menyimpan kredensial instans.
		/// </summary>
		public bool AllowLinkLocalProbe { get; set; }
	}
}
