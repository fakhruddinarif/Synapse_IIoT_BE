using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Entities
{
    /// <summary>
    /// Junction table for many-to-many relationship between StorageFlow and Device
    /// Allows a storage flow to pull data from multiple devices
    /// </summary>
    public class StorageFlowDevice
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid StorageFlowId { get; set; }

        [ForeignKey("StorageFlowId")]
        public StorageFlow StorageFlow { get; set; } = null!;

        [Required]
        public Guid DeviceId { get; set; }

        [ForeignKey("DeviceId")]
        public Device Device { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
