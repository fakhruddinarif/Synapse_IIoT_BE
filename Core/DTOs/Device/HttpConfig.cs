using System.ComponentModel.DataAnnotations;

namespace Core.DTOs
{
    public class HttpConfig
    {
        public string Url { get; set; } = "http://localhost/api/data";
        public string Method { get; set; } = "POST"; // HTTP method, e.g., GET, POST, PUT, DELETE
        public Dictionary<string, string>? Headers { get; set; } // Optional headers for the HTTP request
    }
}