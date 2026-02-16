using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Core.Enums;

namespace Core.Entities
{
	public class Device
	{
		[Key]
		public Guid Id { get; set; } // Unique identifier for the device

		[Required]
		[StringLength(100)]
		[MaxLength(100)]
		public string Name { get; set; } = string.Empty;

		[MaxLength(255)]
		public string? Description { get; set; } = null;

		public bool IsEnabled { get; set; } = false;

		public Protocol Protocol { get; set; } = Protocol.HTTP;

		[Column(TypeName = "json")]
		public string ConnectionConfigJson { get; set; } = "{}";

		public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp for when the user was created
		public DateTime? UpdatedAt { get; set; } = null; // Timestamp for when the user was last updated
		public DateTime? DeletedAt { get; set; } = null; // Timestamp for when the user was deleted (soft delete)
	}
}
