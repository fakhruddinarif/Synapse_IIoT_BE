using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Core.DTOs.StorageFlow
{
    public class CreateStorageFlowDto
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = false;

        [Required]
        [Range(100, int.MaxValue, ErrorMessage = "StorageInterval must be at least 100ms")]
        public int StorageInterval { get; set; } = 1000;

        [Required]
        public Guid MasterTableId { get; set; }

        /// <summary>
        /// List of Device IDs to pull data from
        /// </summary>
        [Required]
        [MinLength(1, ErrorMessage = "At least one device must be selected")]
        public List<Guid> DeviceIds { get; set; } = new();

        /// <summary>
        /// Field mappings from source (device response) to target (master table fields)
        /// </summary>
        [Required]
        [MinLength(1, ErrorMessage = "At least one field mapping must be defined")]
        public List<CreateStorageFlowMappingDto> Mappings { get; set; } = new();
    }

    public class UpdateStorageFlowDto
    {
        [StringLength(100)]
        public string? Name { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }

        public bool? IsActive { get; set; }

        [Range(100, int.MaxValue, ErrorMessage = "StorageInterval must be at least 100ms")]
        public int? StorageInterval { get; set; }

        public Guid? MasterTableId { get; set; }

        public List<Guid>? DeviceIds { get; set; }

        public List<CreateStorageFlowMappingDto>? Mappings { get; set; }
    }

    public class StorageFlowResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int StorageInterval { get; set; }
        public Guid MasterTableId { get; set; }
        public string MasterTableName { get; set; } = string.Empty;
        public List<StorageFlowDeviceDto> Devices { get; set; } = new();
        public List<StorageFlowMappingDto> Mappings { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class StorageFlowDeviceDto
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }

    public class CreateStorageFlowMappingDto
    {
        [Required]
        public Guid MasterTableFieldId { get; set; }

        /// <summary>
        /// JSONPath for HTTP/MQTT (e.g., "$.data.table")
        /// Tag name/address for MODBUS/OPCUA
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// Optional: Tag ID for MODBUS/OPCUA protocols
        /// </summary>
        public Guid? TagId { get; set; }
    }

    public class StorageFlowMappingDto
    {
        public Guid Id { get; set; }
        public Guid MasterTableFieldId { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public string FieldDataType { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public Guid? TagId { get; set; }
        public string? TagName { get; set; }
    }

    /// <summary>
    /// DTO for discovering available fields from device response
    /// Used when user wants to see what fields are available from a device
    /// </summary>
    public class DiscoverFieldsRequestDto
    {
        [Required]
        public Guid DeviceId { get; set; }
    }

    public class DiscoveredFieldDto
    {
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public object? SampleValue { get; set; }
    }
}
