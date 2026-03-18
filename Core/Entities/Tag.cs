using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Core.Enums;
using System.Text;
using DataType = Core.Enums.DataType;

namespace Core.Entities
{
	/// <summary>
	/// Tag Entity - Represents a sensor/memory address mapping for a device
	/// Converts raw device values to engineering units via linear scaling
	/// Part of the Tag Engine layer (data normalization)
	/// </summary>
	public class Tag
	{
		[Key]
		public Guid Id { get; set; } = Guid.NewGuid();

		[Required]
		public Guid DeviceId { get; set; }

		[ForeignKey("DeviceId")]
		public Device? Device { get; set; }

		[Required]
		[MaxLength(100)]
		public string Name { get; set; } = string.Empty; // Example: Temperature_Oven_A

		[Required]
		[MaxLength(100)]
		public string Address { get; set; } = string.Empty; // Example: "40001" for Modbus, "sensor/temperature" for MQTT

		public DataType DataType { get; set; } = DataType.FLOAT;

		public AccessMode AccessMode { get; set; } = AccessMode.READONLY;

		// =============== Linear Scaling Parameters ===============
		public bool IsScaled { get; set; } = false;
		
		/// <summary>
		/// Raw minimum value from device (before scaling)
		/// Example: 0 for 0-4095 ADC range
		/// </summary>
		public double? RawMin { get; set; } = 0; // Value PLC (0)
		
		/// <summary>
		/// Raw maximum value from device (before scaling)
		/// Example: 4095 for 10-bit ADC
		/// </summary>
		public double? RawMax { get; set; } = 4095; // Value PLC (4095)
		
		/// <summary>
		/// Engineered unit minimum after scaling
		/// Example: 0 for 0-100°C temperature range
		/// </summary>
		public double? EuMin { get; set; } = 0;  // Value Engineering (0 Derajat)
		
		/// <summary>
		/// Engineered unit maximum after scaling
		/// Example: 100 for 0-100°C temperature range
		/// </summary>
		public double? EuMax { get; set; } = 100;  // Value Engineering (100 Derajat)

		[MaxLength(20)]
		public string Unit { get; set; } = string.Empty; // "°C", "Bar", "m/s", "%"

		// =============== Current Values (RAM Buffer Cache) ===============
		/// <summary>
		/// Latest raw value received from device (before scaling)
		/// Updated by Fast Loop worker service
		/// </summary>
		public double? CurrentRawValue { get; set; }

		/// <summary>
		/// Latest engineered value after linear scaling
		/// This is what displays on the dashboard
		/// </summary>
		public double? CurrentEngValue { get; set; }

		/// <summary>
		/// Timestamp when current value was last updated
		/// Used by Watchdog to determine if device is online/offline
		/// </summary>
		public DateTime? ValueUpdatedAt { get; set; }

		// ================== Availability & Metadata ==================
		public bool IsActive { get; set; } = true;

		// Integration Optional OPC UA
		[MaxLength(100)]
		public string? OpcUaNodeId { get; set; }

		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
		public DateTime? UpdatedAt { get; set; }
		public DateTime? DeletedAt { get; set; } // Soft delete

		// =============== Helper Methods ===============
		/// <summary>
		/// Apply linear scaling formula to convert raw value to engineering units
		/// Formula: eu_value = eu_min + (raw_value - raw_min) * (eu_max - eu_min) / (raw_max - raw_min)
		/// </summary>
		public double? ScaleRawValue(double? rawValue)
		{
			if (rawValue == null || !IsScaled || RawMin == null || RawMax == null || EuMin == null || EuMax == null)
				return rawValue;

			// Prevent division by zero
			const double epsilon = 0.001;
			if (Math.Abs(RawMax.Value - RawMin.Value) < epsilon)
				return EuMin;

			return EuMin + (rawValue.Value - RawMin.Value) *
				(EuMax.Value - EuMin.Value) / (RawMax.Value - RawMin.Value);
		}

		/// <summary>
		/// Validate scaling parameters
		/// </summary>
		public bool IsScalingValid()
		{
			const double epsilon = 0.001;
			return !IsScaled || (Math.Abs(RawMax - RawMin) >= epsilon && Math.Abs(EuMax - EuMin) >= epsilon);
		}
	}
}
