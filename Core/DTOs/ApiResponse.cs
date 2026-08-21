namespace Core.DTOs
{
	/// <summary>
	/// Envelope WAJIB untuk setiap respons API Synapse. Bentuknya tetap lima field:
	///
	/// <code>{ status, message, data, errors, paging }</code>
	///
	/// Aturan yang tidak boleh dilanggar:
	/// <list type="bullet">
	/// <item><c>errors</c> SELALU array — kosong saat sukses, tidak pernah <c>null</c>.
	/// Klien dengan begitu bisa menulis <c>errors.join()</c> tanpa cek null di setiap
	/// pemanggilan.</item>
	/// <item><c>errors</c> berisi PESAN UNTUK MANUSIA, bukan objek bersarang. Bentuk objek
	/// bebas memaksa frontend menebak strukturnya per endpoint; daftar string bisa
	/// ditampilkan apa adanya di form mana pun.</item>
	/// <item>Kelima field SELALU hadir di JSON, termasuk saat nilainya <c>null</c>.
	/// <c>paging</c> bernilai <c>null</c> pada endpoint yang tidak dipaginasi, bukan
	/// dihilangkan — klien dengan begitu bisa mengandalkan satu bentuk saja, dan
	/// respons dari middleware (401/403/429/500) tidak berbeda bentuk dari respons
	/// controller.</item>
	/// <item><c>status</c> selalu sama dengan status HTTP responsnya.</item>
	/// </list>
	///
	/// Sebelumnya field ini bernama <c>error</c> (tunggal) bertipe <c>object?</c>, dan
	/// isinya berganti bentuk antar endpoint: kadang string, kadang array, kadang objek
	/// anonim <c>{ name = "..." }</c>, kadang detail exception. Frontend tidak punya cara
	/// menanganinya secara seragam selain menebak.
	/// </summary>
	public class ApiResponse<T>
	{
		public int Status { get; set; }

		public string Message { get; set; } = string.Empty;

		public T? Data { get; set; }

		/// <summary>Selalu array. Kosong berarti tidak ada kesalahan.</summary>
		public List<string> Errors { get; set; } = new();

		public PagingInfo? Paging { get; set; }

		/* ------------------------------------------------------------ sukses */

		public static ApiResponse<T> Success(T? data, string message = "Success", PagingInfo? paging = null)
			=> new()
			{
				Status = 200,
				Message = message,
				Data = data,
				Paging = paging,
				Errors = new List<string>()
			};

		public static ApiResponse<T> SuccessWithStatus(int status, T? data, string message = "Success", PagingInfo? paging = null)
			=> new()
			{
				Status = status,
				Message = message,
				Data = data,
				Paging = paging,
				Errors = new List<string>()
			};

		/* ------------------------------------------------------------- gagal */

		public static ApiResponse<T> Fail(int status, string message)
			=> new()
			{
				Status = status,
				Message = message,
				Data = default,
				Paging = null,
				// Pesan utama diulang di daftar errors supaya klien yang hanya membaca
				// `errors` tidak pernah menemukan daftar kosong pada respons gagal.
				Errors = new List<string> { message }
			};

		public static ApiResponse<T> Fail(int status, string message, string error)
			=> new()
			{
				Status = status,
				Message = message,
				Data = default,
				Paging = null,
				Errors = string.IsNullOrWhiteSpace(error)
					? new List<string> { message }
					: new List<string> { error }
			};

		/// <summary>
		/// Gagal, tapi <c>data</c> tetap diisi.
		///
		/// Dipakai kasus seperti "uji koneksi HTTP gagal": operasinya gagal, tapi hasil
		/// diagnostiknya (URL yang dipanggil, status yang diterima, pesan dari server) justru
		/// itulah yang dibutuhkan pengguna untuk memperbaiki konfigurasinya. Membuang data
		/// pada kegagalan semacam ini menghilangkan satu-satunya informasi yang berguna.
		/// </summary>
		public static ApiResponse<T> FailWithData(int status, string message, T? data, IEnumerable<string>? errors = null)
		{
			var list = errors?
				.Where(e => !string.IsNullOrWhiteSpace(e))
				.ToList() ?? new List<string>();

			return new ApiResponse<T>
			{
				Status = status,
				Message = message,
				Data = data,
				Paging = null,
				Errors = list.Count > 0 ? list : new List<string> { message }
			};
		}

		public static ApiResponse<T> Fail(int status, string message, IEnumerable<string>? errors)
		{
			var list = errors?
				.Where(e => !string.IsNullOrWhiteSpace(e))
				.ToList() ?? new List<string>();

			return new ApiResponse<T>
			{
				Status = status,
				Message = message,
				Data = default,
				Paging = null,
				Errors = list.Count > 0 ? list : new List<string> { message }
			};
		}
	}
}
