using Core.DTOs;

namespace Core.Interface
{
	public interface IAuthService
	{
		Task<AuthResponseDto> RegisterAsync(RegisterDto dto);
		Task<(bool Success, string Message, UserInfoDto? UserInfo, string? Token)> LoginAsync(LoginDto dto);
		Task<UserInfoDto?> GetUserInfoAsync(Guid userId);
		string GenerateCsrfToken();
		bool ValidateCsrfToken(string token);
	}
}
