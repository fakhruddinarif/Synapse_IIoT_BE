using System.ComponentModel.DataAnnotations;

namespace Core.DTOs
{
    public class ModbusTcpConfig
    {
        [Required]
        public string IPAddress { get; set; } = "127.0.0.1";

        [Required]
        public int Port { get; set; } = 502;

        [Required]
        public int SlaveId { get; set; } = 1;

        [Required]
        public int ConnectionTimeout { get; set; } = 5000; // Connection timeout in milliseconds
    }
}