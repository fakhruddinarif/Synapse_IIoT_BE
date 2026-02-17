namespace Core.DTOs.Device
{
    /// <summary>
    /// DTO for real-time device data (not stored in database)
    /// </summary>
    public class DeviceDataDto
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty;
        public object Data { get; set; } = new { };
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "success"; // success, error, warning
        public string? Message { get; set; }
    }
}
