using System.ComponentModel.DataAnnotations;
using Core.Enums;

namespace Core.DTOs
{
	public class RegisterDto
	{
		[Required(ErrorMessage = "Username is required")]
		[MaxLength(100, ErrorMessage = "Username cannot exceed 100 characters")]
		public string Username { get; set; } = string.Empty;

		[Required(ErrorMessage = "Password is required")]
		[MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
		public string Password { get; set; } = string.Empty;

		public UserRole Role { get; set; } = UserRole.VIEWER;
	}
}
