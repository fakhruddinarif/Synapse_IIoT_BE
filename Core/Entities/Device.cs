using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json;
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

		public int PollingInterval { get; set; } = 1000; // Polling interval in milliseconds

		public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp for when the user was created
		public DateTime? UpdatedAt { get; set; } = null; // Timestamp for when the user was last updated
		public DateTime? DeletedAt { get; set; } = null; // Timestamp for when the user was deleted (soft delete)

		// Utility method to get the connection configuration as a dictionary
		public readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

		public T? GetConfig<T>()
		{
			if (string.IsNullOrEmpty(ConnectionConfigJson)) return default;

			try
			{
				return JsonSerializer.Deserialize<T>(ConnectionConfigJson, JsonOptions);
			}
			catch 
			{
				return default;
			}
		}
	}
}
