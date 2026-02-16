using Core.Enums;

namespace Core.DTOs
{
	public class UserInfoDto
	{
		public Guid Id { get; set; }
		public string Username { get; set; } = string.Empty;
		public UserRole Role { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime? UpdatedAt { get; set; }
	}
}
