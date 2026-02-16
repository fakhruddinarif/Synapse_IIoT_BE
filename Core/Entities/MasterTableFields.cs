using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Core.Enums;

namespace Core.Entities
{
    public class MasterTableFields
    {
        [Key]
        public Guid Id { get; set; } // Unique identifier for the master table fields

        [Required]
        public Guid MasterTableId { get; set; } // Foreign key to the MasterTable entity
        [ForeignKey("MasterTableId")]
        public MasterTable MasterTable { get; set; } = null!;

        [Required]
        [StringLength(255)]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public DataTypeTable DataType { get; set; } = DataTypeTable.STRING;

        public bool IsEnabled { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp for when the master table field was created
        public DateTime? UpdatedAt { get; set; } = null; // Timestamp for when the master table field was last updated
        public DateTime? DeletedAt { get; set; } = null; // Timestamp for when the master table field was deleted (soft delete)
    }
}