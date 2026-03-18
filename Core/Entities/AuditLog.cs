using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Entities
{
	/// <summary>
	/// Audit Log Entity - Records all data modification events for compliance & security
	/// Part of Security & Authentication Layer requirements
	/// </summary>
	public class AuditLog
	{
		[Key]
		public Guid Id { get; set; } = Guid.NewGuid();

		[ForeignKey("User")]
		public Guid? UserId { get; set; }

		/// <summary>
		/// Action performed: CREATE, UPDATE, DELETE, LOGIN, LOGOUT
		/// </summary>
		[Required]
		[MaxLength(50)]
		public string Action { get; set; } = string.Empty;

		/// <summary>
		/// Entity type affected: Device, Tag, MasterTable, StorageFlow, User, etc
		/// </summary>
		[Required]
		[MaxLength(50)]
		public string EntityType { get; set; } = string.Empty;

		/// <summary>
		/// ID of entity that was modified
		/// </summary>
		public Guid? EntityId { get; set; }

		/// <summary>
		/// JSON serialized old values before update
		/// Only populated for UPDATE actions
		/// </summary>
		[Column(TypeName = "json")]
		public string? OldValues { get; set; }

		/// <summary>
		/// JSON serialized new values after update
		/// Only populated for UPDATE and CREATE actions
		/// </summary>
		[Column(TypeName = "json")]
		public string? NewValues { get; set; }

		/// <summary>
		/// IP address of the requestor
		/// </summary>
		[MaxLength(45)] // IPv6 max length
		public string? IpAddress { get; set; }

		/// <summary>
		/// User agent string (browser/client info)
		/// </summary>
		[MaxLength(500)]
		public string? UserAgent { get; set; }

		/// <summary>
		/// Result of action: SUCCESS or FAILURE
		/// </summary>
		[Required]
		public AuditStatus Status { get; set; } = AuditStatus.SUCCESS;

		/// <summary>
		/// Error message if action failed
		/// </summary>
		[MaxLength(500)]
		public string? ErrorMessage { get; set; }

		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		// Navigation
		public User? User { get; set; }
	}

	public enum AuditStatus
	{
		SUCCESS,
		FAILURE
	}
}
