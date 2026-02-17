using System.ComponentModel.DataAnnotations;

namespace Core.DTOs
{
    public class MqttConfig
    {
        public string Protocol { get; set; } = "mqtt"; // Protocol type, e.g., "mqtt", "mqtts", "ws", "wss"
        public string BrokerUrl { get; set; } = "localhost";
        public int Port { get; set; } = 1883;
        public string ClientId { get; set; } = Guid.NewGuid().ToString();
        public string Topic { get; set; } = "#";
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool UseTls { get; set; } = false;
    }
}