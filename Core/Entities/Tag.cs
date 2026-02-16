using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Core.Enums;
using System.Text;
using DataType = Core.Enums.DataType;

namespace Core.Entities
{
	public class Tag
	{
		[Key]
		public Guid Id { get; set; }

		[Required]
		public Guid DeviceId { get; set; }

		[ForeignKey("DeviceId")]
		public Device? Device { get; set; }

		[Required]
		[MaxLength(100)]
		public string Name { get; set; } = string.Empty; // Example: Temp_Sensor_1

		[Required]
		[MaxLength(100)]
		public string Address { get; set; } = string.Empty; // Example: "40001" for Modbus, "sensor/temperature" for MQTT

		public DataType DataType { get; set; } = DataType.FLOAT;

		public AccessMode AccessMode { get; set; } = AccessMode.READONLY;

		public bool IsScaled { get; set; } = false;
		public double? RawMin { get; set; } // Value PLC (0)
		public double? RawMax { get; set; } // Value PLC (4095)
		public double? EuMin { get; set; }  // Value Engineering (0 Derajat)
		public double? EuMax { get; set; }  // Value Engineering (100 Derajat)

		[MaxLength(20)]
		public string Unit { get; set; } = string.Empty; // "°C", "Bar"

		// Integration Optional OPC UA
		[MaxLength(100)]
		public string? OpcUaNodeId { get; set; }

		public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp for when the user was created
		public DateTime? UpdatedAt { get; set; } = null; // Timestamp for when the user was last updated
		public DateTime? DeletedAt { get; set; } = null; // Timestamp for when the user was deleted (soft delete)
	}
}
