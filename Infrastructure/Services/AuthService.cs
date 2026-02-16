using Core.DTOs;
using Core.Entities;
using Core.Interface;
using System.Security.Cryptography;

namespace Infrastructure.Services
{
	public class AuthService : IAuthService
	{
		private readonly IUserRepository _userRepository;
		private readonly ITokenService _tokenService;
		private static readonly Dictionary<string, DateTime> _csrfTokens = new();
		private static readonly TimeSpan _csrfTokenLifetime = TimeSpan.FromHours(1);

		public AuthService(IUserRepository userRepository, ITokenService tokenService)
		{
			_userRepository = userRepository;
			_tokenService = tokenService;
		}

		public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
		{
			// Check if username already exists
			if (await _userRepository.UsernameExistsAsync(dto.Username))
			{
				return new AuthResponseDto
				{
					Status = 400,
					Message = "Username already exists",
					Error = new { username = "Username already exists" }
				};
			}

			// Hash password
			var passwordHash = HashPassword(dto.Password);

			// Create new user
			var user = new User
			{
				Id = Guid.NewGuid(),
				Username = dto.Username,
				PasswordHash = passwordHash,
				Role = dto.Role,
				CreatedAt = DateTime.UtcNow
			};

			await _userRepository.CreateAsync(user);

			return new AuthResponseDto
			{
				Status = 201,
				Message = "User registered successfully",
				Data = MapToUserInfoDto(user),
				Error = null
			};
		}

		public async Task<(bool Success, string Message, UserInfoDto? UserInfo, string? Token)> LoginAsync(LoginDto dto)
		{
			// Check if user exists
			var user = await _userRepository.GetByUsernameAsync(dto.Username);
			if (user == null)
			{
				return (false, "Invalid username or password", null, null);
			}

			// Verify password
			if (!VerifyPassword(dto.Password, user.PasswordHash))
			{
				return (false, "Invalid username or password", null, null);
			}

			// Generate JWT token
			var token = _tokenService.GenerateJwtToken(user.Id, user.Username, user.Role.ToString());

			return (true, "Login successful", MapToUserInfoDto(user), token);
		}

		public async Task<UserInfoDto?> GetUserInfoAsync(Guid userId)
		{
			var user = await _userRepository.GetByIdAsync(userId);
			if (user == null)
			{
				return null;
			}

			return MapToUserInfoDto(user);
		}

		public string GenerateCsrfToken()
		{
			var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
			_csrfTokens[token] = DateTime.UtcNow.Add(_csrfTokenLifetime);
			
			// Clean up expired tokens
			CleanupExpiredTokens();
			
			return token;
		}

		public bool ValidateCsrfToken(string token)
		{
			if (string.IsNullOrEmpty(token))
				return false;

			if (_csrfTokens.TryGetValue(token, out var expiration))
			{
				if (DateTime.UtcNow <= expiration)
				{
					return true;
				}
				else
				{
					_csrfTokens.Remove(token);
					return false;
				}
			}

			return false;
		}

		private static void CleanupExpiredTokens()
		{
			var expiredTokens = _csrfTokens.Where(kvp => DateTime.UtcNow > kvp.Value).Select(kvp => kvp.Key).ToList();
			foreach (var token in expiredTokens)
			{
				_csrfTokens.Remove(token);
			}
		}

		private static string HashPassword(string password)
		{
			return BCrypt.Net.BCrypt.HashPassword(password);
		}

		private static bool VerifyPassword(string password, string hash)
		{
			return BCrypt.Net.BCrypt.Verify(password, hash);
		}

		private UserInfoDto MapToUserInfoDto(User user)
		{
			return new UserInfoDto
			{
				Id = user.Id,
				Username = user.Username,
				Role = user.Role,
				CreatedAt = user.CreatedAt,
				UpdatedAt = user.UpdatedAt
			};
		}
	}
}
