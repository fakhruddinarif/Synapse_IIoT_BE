namespace Core.DTOs.Device
{
    /// <summary>
    /// DTO for testing external HTTP APIs
    /// </summary>
    public class TestHttpRequestDto
    {
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = "GET";
        public Dictionary<string, string>? Headers { get; set; }
        public string? Body { get; set; }
    }
}
