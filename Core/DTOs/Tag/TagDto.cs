using System;
using System.ComponentModel.DataAnnotations;
using Core.Entities;
using Core.Enums;
using DataType = Core.Enums.DataType;

namespace Core.DTOs.Tag
{
	/// <summary>
	/// Request DTO for creating a new tag
	/// Includes linear scaling parameters for sensor value conversion
	/// </summary>
	public class CreateTagDto
	{
		[Required(ErrorMessage = "Device ID is required")]
		public Guid DeviceId { get; set; }

		[Required(ErrorMessage = "Tag name is required")]
		[StringLength(100, MinimumLength = 3, ErrorMessage = "Name must be between 3 and 100 characters")]
		public string Name { get; set; } = string.Empty;

		[Required(ErrorMessage = "Device address is required")]
		[StringLength(100, ErrorMessage = "Address must not exceed 100 characters")]
		public string Address { get; set; } = string.Empty;

		[Required(ErrorMessage = "Data type is required")]
		public DataType DataType { get; set; } = DataType.FLOAT;

		[Required(ErrorMessage = "Access mode is required")]
		public AccessMode AccessMode { get; set; } = AccessMode.READONLY;

		[Range(0, double.MaxValue, ErrorMessage = "RawMin must be non-negative")]
		public double RawMin { get; set; } = 0;

		[Range(0, double.MaxValue, ErrorMessage = "RawMax must be non-negative")]
		public double RawMax { get; set; } = 4095;

		[Range(double.MinValue, double.MaxValue, ErrorMessage = "EuMin must be a valid number")]
		public double EuMin { get; set; } = 0;

		[Range(double.MinValue, double.MaxValue, ErrorMessage = "EuMax must be a valid number")]
		public double EuMax { get; set; } = 100;

		[StringLength(20, ErrorMessage = "Unit must not exceed 20 characters")]
		public string? Unit { get; set; }

		[StringLength(256, ErrorMessage = "OPC UA Node ID must not exceed 256 characters")]
		public string? OpcUaNodeId { get; set; }
	}

	/// <summary>
	/// Request DTO for updating an existing tag
	/// All fields are optional to support partial updates
	/// </summary>
	public class UpdateTagDto
	{
		[StringLength(100, MinimumLength = 3, ErrorMessage = "Name must be between 3 and 100 characters")]
		public string? Name { get; set; }

		[StringLength(100, ErrorMessage = "Address must not exceed 100 characters")]
		public string? Address { get; set; }

		public DataType? DataType { get; set; }

		public AccessMode? AccessMode { get; set; }

		[Range(0, double.MaxValue, ErrorMessage = "RawMin must be non-negative")]
		public double? RawMin { get; set; }

		[Range(0, double.MaxValue, ErrorMessage = "RawMax must be non-negative")]
		public double? RawMax { get; set; }

		[Range(double.MinValue, double.MaxValue, ErrorMessage = "EuMin must be a valid number")]
		public double? EuMin { get; set; }

		[Range(double.MinValue, double.MaxValue, ErrorMessage = "EuMax must be a valid number")]
		public double? EuMax { get; set; }

		[StringLength(20, ErrorMessage = "Unit must not exceed 20 characters")]
		public string? Unit { get; set; }

		[StringLength(256, ErrorMessage = "OPC UA Node ID must not exceed 256 characters")]
		public string? OpcUaNodeId { get; set; }
	}

	/// <summary>
	/// Response DTO for tag API responses
	/// Includes readonly fields computed from entity data
	/// </summary>
	public class TagResponseDto
	{
		public int Status { get; set; } = 200;
		public string Message { get; set; } = "Success";
		public Guid Id { get; set; }
		public Guid DeviceId { get; set; }
		public string Name { get; set; } = string.Empty;
		public string Address { get; set; } = string.Empty;
		public DataType DataType { get; set; }
		public AccessMode AccessMode { get; set; }
		public double RawMin { get; set; }
		public double RawMax { get; set; }
		public double EuMin { get; set; }
		public double EuMax { get; set; }
		public string? Unit { get; set; }
		public string? OpcUaNodeId { get; set; }

		/// <summary>
		/// Scaling factor: ratio of EU range to raw range
		/// Used for visualization of scaling ratio
		/// </summary>
		public double ScalingFactor { get; set; }

		public DateTime CreatedAt { get; set; }
		public bool IsDeleted { get; set; }
	}

	/// <summary>
	/// Request DTO for tag filtering and search operations
	/// </summary>
	public class TagFilterDto
	{
		public Guid? DeviceId { get; set; }
		public string? SearchTerm { get; set; }
		public DataType? DataType { get; set; }
		public int Page { get; set; } = 1;
		public int PageSize { get; set; } = 50;
	}
}
