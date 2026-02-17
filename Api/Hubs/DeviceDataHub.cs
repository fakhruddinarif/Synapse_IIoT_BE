using Microsoft.AspNetCore.SignalR;

namespace Api.Hubs
{
    /// <summary>
    /// SignalR Hub for broadcasting real-time device data to connected clients
    /// </summary>
    public class DeviceDataHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", new 
            { 
                connectionId = Context.ConnectionId,
                message = "Successfully connected to DeviceDataHub"
            });
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Client can subscribe to specific device updates
        /// </summary>
        public async Task SubscribeToDevice(string deviceId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"device_{deviceId}");
            await Clients.Caller.SendAsync("Subscribed", new { deviceId });
        }

        /// <summary>
        /// Client can unsubscribe from device updates
        /// </summary>
        public async Task UnsubscribeFromDevice(string deviceId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"device_{deviceId}");
            await Clients.Caller.SendAsync("Unsubscribed", new { deviceId });
        }
    }
}
