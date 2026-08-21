using Core.DTOs;
using Core.Entities;
using Core.Interface;

namespace Infrastructure.Services
{
	public class AuthService : IAuthService
	{
		private readonly IUserRepository _userRepository;
		private readonly ITokenService _tokenService;

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
					Message = "Username sudah terpakai",
					Errors = new List<string> { "Username sudah terpakai. Pilih username lain." }
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
				Message = "User berhasil didaftarkan",
				Data = MapToUserInfoDto(user)
			};
		}

		public async Task<(bool Success, string Message, UserInfoDto? UserInfo, string? Token)> LoginAsync(LoginDto dto)
		{
			var user = await _userRepository.GetByUsernameAsync(dto.Username);

			if (user == null)
			{
				// Verifikasi tetap dijalankan terhadap hash tiruan.
				//
				// Tanpa ini, username yang tidak ada dijawab seketika sementara username yang
				// ada menanggung ~250 ms perhitungan BCrypt. Selisih itu cukup untuk memetakan
				// akun mana yang benar-benar ada — enumerasi pengguna lewat perbedaan waktu,
				// meski pesan kesalahannya sudah dibuat sama.
				VerifyPassword(dto.Password, DummyHash);
				return (false, "Username atau password salah", null, null);
			}

			if (!VerifyPassword(dto.Password, user.PasswordHash))
			{
				return (false, "Username atau password salah", null, null);
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

		/// <summary>
		/// Work factor 12. Cukup lambat untuk membuat brute force offline mahal bila database
		/// bocor, masih cukup cepat untuk login interaktif. Default pustaka (11) dinaikkan
		/// satu tingkat karena biaya login bukan jalur panas di sistem ini.
		/// </summary>
		private const int BcryptWorkFactor = 12;

		/// <summary>
		/// Hash tiruan untuk menyamakan waktu jawaban saat username tidak ditemukan. Nilainya
		/// hash BCrypt sah atas string tetap, jadi verifikasinya menghabiskan waktu yang sama
		/// dengan verifikasi sungguhan.
		/// </summary>
		private static readonly string DummyHash =
			BCrypt.Net.BCrypt.HashPassword("synapse-timing-equalizer", BcryptWorkFactor);

		private static string HashPassword(string password)
		{
			return BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
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
