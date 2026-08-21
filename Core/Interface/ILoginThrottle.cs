namespace Core.Interface
{
	/// <summary>
	/// Pembatas percobaan login untuk melawan brute force dan credential stuffing.
	///
	/// Melengkapi rate limiter ASP.NET yang membatasi per alamat IP. Keduanya diperlukan
	/// karena keduanya menutup serangan yang berbeda:
	/// <list type="bullet">
	/// <item>Rate limiter per IP menghentikan satu penyerang mencoba banyak password.</item>
	/// <item>Throttle per username menghentikan banyak IP (botnet, proksi berputar)
	/// mencoba satu akun.</item>
	/// </list>
	/// </summary>
	public interface ILoginThrottle
	{
		/// <summary>
		/// Memeriksa apakah kombinasi username/IP sedang terkunci. <c>RetryAfter</c> adalah
		/// sisa waktu kunci, untuk diteruskan ke klien sebagai <c>Retry-After</c>.
		/// </summary>
		(bool IsLocked, TimeSpan RetryAfter) Check(string username, string? ipAddress);

		/// <summary>Mencatat satu kegagalan. Mengembalikan sisa percobaan sebelum terkunci.</summary>
		int RegisterFailure(string username, string? ipAddress);

		/// <summary>Menghapus hitungan setelah login berhasil.</summary>
		void Reset(string username, string? ipAddress);
	}
}
