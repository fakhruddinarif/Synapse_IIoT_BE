using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Entities
{
    /// <summary>
    /// Defines the mapping between source data (from device response) and target field (in MasterTable)
    /// Supports JSONPath for extracting data from HTTP/MQTT responses
    /// For MODBUS/OPCUA, SourcePath refers to Tag.Name or Tag.Address
    /// </summary>
    public class StorageFlowMapping
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid StorageFlowId { get; set; }

        [ForeignKey("StorageFlowId")]
        public StorageFlow StorageFlow { get; set; } = null!;

        [Required]
        public Guid MasterTableFieldId { get; set; }

        [ForeignKey("MasterTableFieldId")]
        public MasterTableFields MasterTableField { get; set; } = null!;

        /// <summary>
        /// JSONPath expression for HTTP/MQTT responses (e.g., "$.data.table", "$.data.desc")
        /// For MODBUS/OPCUA, this is the Tag Name or Tag Address
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// Optional: Reference to Tag entity for MODBUS/OPCUA protocols
        /// </summary>
        public Guid? TagId { get; set; }

        [ForeignKey("TagId")]
        public Tag? Tag { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
