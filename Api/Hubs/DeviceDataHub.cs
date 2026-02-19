using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace Api.Hubs
{
    /// <summary>
    /// SignalR Hub for broadcasting real-time device data to connected clients.
    /// Supports device-specific subscriptions for selective data streaming.
    /// Group naming uses configurable prefix from appsettings for security.
    /// </summary>
    public class DeviceDataHub : Hub
    {
        private readonly IConfiguration _configuration;
        private readonly string _deviceGroupPrefix;

        public DeviceDataHub(IConfiguration configuration)
        {
            _configuration = configuration;
            _deviceGroupPrefix = _configuration["SignalRSettings:GroupPrefix:Device"] ?? "device_";
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", new 
            { 
                connectionId = Context.ConnectionId,
                message = "Successfully connected to DeviceDataHub",
                timestamp = DateTime.UtcNow
            });
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Log disconnection if needed
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Client can subscribe to specific device updates.
        /// This allows selective real-time data streaming instead of receiving all devices.
        /// </summary>
        /// <param name="deviceId">Device GUID to subscribe to</param>
        public async Task SubscribeToDevice(string deviceId)
        {
            var groupName = $"{_deviceGroupPrefix}{deviceId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            await Clients.Caller.SendAsync("Subscribed", new 
            { 
                deviceId, 
                groupName,
                message = $"Successfully subscribed to device {deviceId}",
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Client can unsubscribe from device updates
        /// </summary>
        /// <param name="deviceId">Device GUID to unsubscribe from</param>
        public async Task UnsubscribeFromDevice(string deviceId)
        {
            var groupName = $"{_deviceGroupPrefix}{deviceId}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            await Clients.Caller.SendAsync("Unsubscribed", new 
            { 
                deviceId,
                groupName,
                message = $"Successfully unsubscribed from device {deviceId}",
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Get current connection info
        /// </summary>
        public async Task GetConnectionInfo()
        {
            await Clients.Caller.SendAsync("ConnectionInfo", new
            {
                connectionId = Context.ConnectionId,
                timestamp = DateTime.UtcNow
            });
        }
    }
}
