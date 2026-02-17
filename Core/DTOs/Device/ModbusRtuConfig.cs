using System.ComponentModel.DataAnnotations;

namespace Core.DTOs
{
    public class ModbusRtuConfig
    {
        [Required]
        public string PortName { get; set; } = "COM1";

        [Required]
        public int BaudRate { get; set; } = 9600;

        [Required]
        public int DataBits { get; set; } = 8;

        [Required]
        public int StopBits { get; set; } = 1;

        [Required]
        public string Parity { get; set; } = "None"; // Options: None, Odd, Even, Mark, Space

        [Required]
        public int SlaveId { get; set; } = 1;
    }
}