namespace Core.DTOs
{
	/// <summary>
	/// Hasil operasi autentikasi di lapisan service. Bentuknya mengikuti envelope API
	/// (<c>status</c>, <c>message</c>, <c>data</c>, <c>errors</c>) supaya controller cukup
	/// meneruskannya tanpa menerjemahkan bentuk.
	///
	/// <c>Errors</c> berupa daftar string, bukan <c>object?</c>: nilai objek anonim seperti
	/// <c>new { username = "..." }</c> memaksa frontend menebak strukturnya per endpoint.
	/// </summary>
	public class AuthResponseDto
	{
		public int Status { get; set; }
		public string Message { get; set; } = string.Empty;
		public UserInfoDto? Data { get; set; }
		public List<string> Errors { get; set; } = new();
	}
}
