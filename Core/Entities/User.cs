using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Core.Enums;

namespace Core.Entities
{
	public class User
	{
		[Key]
		public Guid Id { get; set; } // Unique identifier for the user

		[Required]
		[MaxLength(100)]
		public string Username { get; set; } = string.Empty;

		[Required]
		[MaxLength(255)]
		public string PasswordHash { get; set; } = string.Empty; // Hashed password for security

		public UserRole Role { get; set; } = UserRole.VIEWER; // Role of the user, determining their permissions

		public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp for when the user was created
		public DateTime? UpdatedAt { get; set; } = null; // Timestamp for when the user was last updated
		public DateTime? DeletedAt { get; set; } = null; // Timestamp for when the user was deleted (soft delete)
	}
}
