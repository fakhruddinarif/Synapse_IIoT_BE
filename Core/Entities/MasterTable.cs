using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Core.Enums;

namespace Core.Entities
{
    public class MasterTable
    {
        [Key]
        public Guid Id { get; set; } // Unique identifier for the master table

        [Required]
        [StringLength(200)]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        [MaxLength(255)]
        public string TableName { get; set; } = string.Empty;

        public string? Description { get; set; } = null;

        public bool IsActive { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp for when the user was created
		public DateTime? UpdatedAt { get; set; } = null; // Timestamp for when the user was last updated
		public DateTime? DeletedAt { get; set; } = null; // Timestamp for when the user was deleted (soft delete)

        // Navigation property for related MasterTableFields
        public ICollection<MasterTableFields> Fields { get; set; } = new List<MasterTableFields>();
        public ICollection<StorageFlow> StorageFlows { get; set; } = new List<StorageFlow>(); // Navigation property for related StorageFlows

    }
}