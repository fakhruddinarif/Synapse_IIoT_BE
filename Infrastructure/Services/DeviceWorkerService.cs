using Core.DTOs;
using Core.DTOs.Device;
using Core.Entities;
using Core.Enums;
using Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace Infrastructure.Services
{
    /// <summary>
    /// Background service that polls enabled devices based on their polling interval
    /// and sends data to clients via SignalR (no database storage)
    /// </summary>
    /// <typeparam name="THub">The SignalR Hub type for broadcasting data</typeparam>
    public class DeviceWorkerService<THub> : BackgroundService where THub : Hub
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<THub> _hubContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DeviceWorkerService<THub>> _logger;
        private readonly Dictionary<Guid, CancellationTokenSource> _deviceTasks = new();

        public DeviceWorkerService(
            IServiceProvider serviceProvider,
            IHubContext<THub> hubContext,
            IHttpClientFactory httpClientFactory,
            ILogger<DeviceWorkerService<THub>> logger)
        {
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DeviceWorkerService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Get all enabled devices
                    var enabledDevices = await dbContext.Devices
                        .Where(d => d.IsEnabled && d.DeletedAt == null)
                        .ToListAsync(stoppingToken);

                    // Start tasks for new devices or restart if config changed
                    foreach (var device in enabledDevices)
                    {
                        if (!_deviceTasks.ContainsKey(device.Id))
                        {
                            _logger.LogInformation($"Starting polling for device: {device.Name} ({device.Id})");
                            var cts = new CancellationTokenSource();
                            _deviceTasks[device.Id] = cts;

                            // Start device polling in background
                            _ = Task.Run(() => PollDeviceAsync(device, cts.Token), stoppingToken);
                        }
                    }

                    // Stop tasks for disabled devices
                    var deviceIdsToRemove = _deviceTasks.Keys
                        .Where(id => !enabledDevices.Any(d => d.Id == id))
                        .ToList();

                    foreach (var deviceId in deviceIdsToRemove)
                    {
                        _logger.LogInformation($"Stopping polling for device: {deviceId}");
                        await _deviceTasks[deviceId].CancelAsync();
                        _deviceTasks.Remove(deviceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in DeviceWorkerService main loop");
                }

                // Check for device changes every 10 seconds
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private async Task PollDeviceAsync(Device device, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Polling started for {device.Name} with interval {device.PollingInterval}ms");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    DeviceDataDto? data = device.Protocol switch
                    {
                        Protocol.HTTP => await PollHttpDeviceAsync(device),
                        Protocol.MQTT => await PollMqttDeviceAsync(device),
                        _ => null
                    };

                    if (data != null)
                    {
                        // Broadcast to all clients
                        await _hubContext.Clients.All.SendAsync("ReceiveDeviceData", data, cancellationToken);

                        // Also broadcast to specific device group
                        await _hubContext.Clients.Group($"device_{device.Id}")
                            .SendAsync("ReceiveDeviceData", data, cancellationToken);

                        _logger.LogDebug($"Data sent for device {device.Name}: {JsonSerializer.Serialize(data.Data)}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error polling device {device.Name}");

                    // Send error to clients
                    var errorData = new DeviceDataDto
                    {
                        DeviceId = device.Id,
                        DeviceName = device.Name,
                        Protocol = device.Protocol.ToString(),
                        Data = new { },
                        Status = "error",
                        Message = ex.Message,
                        Timestamp = DateTime.UtcNow
                    };

                    await _hubContext.Clients.All.SendAsync("ReceiveDeviceData", errorData, cancellationToken);
                }

                // Wait for the polling interval
                await Task.Delay(device.PollingInterval, cancellationToken);
            }

            _logger.LogInformation($"Polling stopped for {device.Name}");
        }

        private async Task<DeviceDataDto?> PollHttpDeviceAsync(Device device)
        {
            var config = device.GetConfig<HttpConfig>();
            if (config == null)
            {
                throw new InvalidOperationException($"Invalid HTTP config for device {device.Name}");
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            // Add custom headers if any
            if (config.Headers != null)
            {
                foreach (var header in config.Headers)
                {
                    client.DefaultRequestHeaders.Add(header.Key, header.Value);
                }
            }

            HttpResponseMessage response = config.Method.ToUpper() switch
            {
                "GET" => await client.GetAsync(config.Url),
                "POST" => await client.PostAsync(config.Url, null),
                "PUT" => await client.PutAsync(config.Url, null),
                "DELETE" => await client.DeleteAsync(config.Url),
                _ => throw new InvalidOperationException($"Unsupported HTTP method: {config.Method}")
            };

            response.EnsureSuccessStatusCode();

            var responseData = await response.Content.ReadAsStringAsync();
            var jsonData = JsonSerializer.Deserialize<object>(responseData);

            return new DeviceDataDto
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                Protocol = device.Protocol.ToString(),
                Data = jsonData ?? new { },
                Status = "success",
                Timestamp = DateTime.UtcNow
            };
        }

        private Task<DeviceDataDto?> PollMqttDeviceAsync(Device device)
        {
            var config = device.GetConfig<MqttConfig>();
            if (config == null)
            {
                throw new InvalidOperationException($"Invalid MQTT config for device {device.Name}");
            }

            // For MQTT, we'll simulate receiving data with random values
            // In production, you would maintain persistent MQTT connections
            
            var random = new Random();
            var simulatedData = new
            {
                topic = config.Topic,
                temperature = Math.Round(20 + random.NextDouble() * 15, 2),
                humidity = Math.Round(40 + random.NextDouble() * 40, 2),
                pressure = Math.Round(1000 + random.NextDouble() * 50, 2)
            };

            var result = new DeviceDataDto
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                Protocol = device.Protocol.ToString(),
                Data = simulatedData,
                Status = "success",
                Message = "Simulated MQTT data",
                Timestamp = DateTime.UtcNow
            };

            return Task.FromResult<DeviceDataDto?>(result);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("DeviceWorkerService stopping...");

            // Cancel all device polling tasks
            foreach (var cts in _deviceTasks.Values)
            {
                await cts.CancelAsync();
            }

            _deviceTasks.Clear();

            await base.StopAsync(cancellationToken);
        }
    }
}
