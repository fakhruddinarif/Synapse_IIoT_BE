namespace Core.DTOs
{
	public class AuthResponseDto
	{
		public int Status { get; set; }
		public string Message { get; set; } = string.Empty;
		public UserInfoDto? Data { get; set; }
		public object? Error { get; set; }
	}
}
