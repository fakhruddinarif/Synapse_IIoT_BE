using System.ComponentModel.DataAnnotations;

namespace Core.DTOs
{
    public class OpcUaConfig
    {
        public string EndpointUrl { get; set; } = "opc.tcp://localhost";
        public int Port { get; set; } = 4840;
        public string SecurityPolicy { get; set; } = "None";
        public string SecurityMode { get; set; } = "None";
        public string AuthType { get; set; } = "Anonymous";
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}