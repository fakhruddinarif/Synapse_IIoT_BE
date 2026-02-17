using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Core.Enums;

namespace Core.Entities
{
    public class StorageFlow
    {
        [Key]
        public Guid Id { get; set; } // Unique identifier for the storage flow

        [Required]
        [StringLength(100)]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? Description { get; set; } = null;
        public bool IsActive { get; set; } = false;
        public int StorageInterval { get; set; } = 1000; // Storage interval in milliseconds
        public Guid MasterTableId { get; set; } // Foreign key to MasterTable
        public MasterTable MasterTable { get; set; } = null!; // Navigation property to MasterTable

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp for when the user was created
        public DateTime? UpdatedAt { get; set; } = null; // Timestamp for when the user was last updated
        public DateTime? DeletedAt { get; set; } = null; // Timestamp for when the user was deleted (soft delete)
    }
}