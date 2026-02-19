namespace Core.DTOs.Device
{
    /// <summary>
    /// Response DTO for HTTP connection testing
    /// </summary>
    public class TestHttpConnectionResponseDto
    {
        public string RequestUrl { get; set; } = string.Empty;
        public string RequestMethod { get; set; } = string.Empty;
        public int ResponseStatusCode { get; set; }
        public object? ResponseData { get; set; }
        public Dictionary<string, string>? ResponseHeaders { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
