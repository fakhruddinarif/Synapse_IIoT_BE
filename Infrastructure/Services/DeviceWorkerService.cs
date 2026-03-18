using Core.DTOs;
using Core.DTOs.Device;
using Core.Entities;
using Core.Enums;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using System.Data.Common;
using System.Threading.Channels;

namespace Infrastructure.Services
{
    /// <summary>
    /// Event-driven background service that manages device polling and storage flows.
    /// Uses Channels for real-time updates when devices or storage flows are created/updated/deleted.
    /// </summary>
    /// <typeparam name="THub">The SignalR Hub type for broadcasting data</typeparam>
    public class DeviceWorkerService<THub> : BackgroundService, IDeviceWorkerService where THub : Hub
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<THub> _hubContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DeviceWorkerService<THub>> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _deviceGroupPrefix;
        private readonly Dictionary<Guid, CancellationTokenSource> _deviceTasks = new();
        private readonly Dictionary<Guid, CancellationTokenSource> _storageFlowTasks = new();
        
        // Channels for event-driven updates
        private readonly Channel<Guid> _deviceRefreshChannel = Channel.CreateUnbounded<Guid>();
        private readonly Channel<Guid> _deviceRemoveChannel = Channel.CreateUnbounded<Guid>();
        private readonly Channel<Guid> _storageFlowRefreshChannel = Channel.CreateUnbounded<Guid>();
        private readonly Channel<Guid> _storageFlowRemoveChannel = Channel.CreateUnbounded<Guid>();
        private readonly Channel<bool> _refreshAllChannel = Channel.CreateUnbounded<bool>();

        public DeviceWorkerService(
            IServiceProvider serviceProvider,
            IHubContext<THub> hubContext,
            IHttpClientFactory httpClientFactory,
            ILogger<DeviceWorkerService<THub>> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
            _deviceGroupPrefix = _configuration["SignalRSettings:GroupPrefix:Device"] ?? "device_";
        }

        #region IDeviceWorkerService Implementation

        public async Task RefreshDeviceAsync(Guid deviceId)
        {
            await _deviceRefreshChannel.Writer.WriteAsync(deviceId);
            _logger.LogInformation($"[Event] Device refresh triggered for: {deviceId}");
        }

        public async Task RemoveDeviceAsync(Guid deviceId)
        {
            await _deviceRemoveChannel.Writer.WriteAsync(deviceId);
            _logger.LogInformation($"[Event] Device removal triggered for: {deviceId}");
        }

        public async Task RefreshStorageFlowAsync(Guid storageFlowId)
        {
            await _storageFlowRefreshChannel.Writer.WriteAsync(storageFlowId);
            _logger.LogInformation($"[Event] Storage flow refresh triggered for: {storageFlowId}");
        }

        public async Task RemoveStorageFlowAsync(Guid storageFlowId)
        {
            await _storageFlowRemoveChannel.Writer.WriteAsync(storageFlowId);
            _logger.LogInformation($"[Event] Storage flow removal triggered for: {storageFlowId}");
        }

        public async Task RefreshAllAsync()
        {
            await _refreshAllChannel.Writer.WriteAsync(true);
            _logger.LogInformation("[Event] Refresh all triggered");
        }

        #endregion

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DeviceWorkerService started with event-driven approach");

            // Initial load of all devices and storage flows
            await InitialLoadAsync(stoppingToken);

            // Listen to events from channels
            _ = Task.Run(() => ListenToDeviceRefreshEventsAsync(stoppingToken), stoppingToken);
            _ = Task.Run(() => ListenToDeviceRemoveEventsAsync(stoppingToken), stoppingToken);
            _ = Task.Run(() => ListenToStorageFlowRefreshEventsAsync(stoppingToken), stoppingToken);
            _ = Task.Run(() => ListenToStorageFlowRemoveEventsAsync(stoppingToken), stoppingToken);
            _ = Task.Run(() => ListenToRefreshAllEventsAsync(stoppingToken), stoppingToken);

            // Keep service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task InitialLoadAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Loading all enabled devices and active storage flows...");

                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Load all enabled devices
                var enabledDevices = await dbContext.Devices
                    .Where(d => d.IsEnabled && d.DeletedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var device in enabledDevices)
                {
                    await StartDevicePollingAsync(device, cancellationToken);
                }

                // Load all active storage flows
                await LoadStorageFlowsAsync(dbContext, cancellationToken);

                _logger.LogInformation($"Initial load completed: {enabledDevices.Count} devices, {_storageFlowTasks.Count} storage flows");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during initial load");
            }
        }

        private async Task ListenToDeviceRefreshEventsAsync(CancellationToken cancellationToken)
        {
            await foreach (var deviceId in _deviceRefreshChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var device = await dbContext.Devices
                        .Where(d => d.Id == deviceId && d.DeletedAt == null)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (device != null && device.IsEnabled)
                    {
                        // Stop existing task if any
                        if (_deviceTasks.TryGetValue(deviceId, out var existingCts))
                        {
                            await existingCts.CancelAsync();
                            _deviceTasks.Remove(deviceId);
                        }

                        // Start new task
                        await StartDevicePollingAsync(device, cancellationToken);
                    }
                    else
                    {
                        // Device not found or disabled, remove it
                        await RemoveDevicePollingAsync(deviceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error refreshing device {deviceId}");
                }
            }
        }

        private async Task ListenToDeviceRemoveEventsAsync(CancellationToken cancellationToken)
        {
            await foreach (var deviceId in _deviceRemoveChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await RemoveDevicePollingAsync(deviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error removing device {deviceId}");
                }
            }
        }

        private async Task ListenToStorageFlowRefreshEventsAsync(CancellationToken cancellationToken)
        {
            await foreach (var flowId in _storageFlowRefreshChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var flow = await dbContext.StorageFlows
                        .Where(sf => sf.Id == flowId && sf.DeletedAt == null)
                        .Include(sf => sf.MasterTable)
                            .ThenInclude(mt => mt.Fields.Where(f => f.DeletedAt == null && f.IsEnabled))
                        .Include(sf => sf.StorageFlowDevices)
                            .ThenInclude(sfd => sfd.Device)
                        .Include(sf => sf.StorageFlowMappings)
                            .ThenInclude(sfm => sfm.MasterTableField)
                        .Include(sf => sf.StorageFlowMappings)
                            .ThenInclude(sfm => sfm.Tag)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (flow != null && flow.IsActive)
                    {
                        // Stop existing task if any
                        if (_storageFlowTasks.TryGetValue(flowId, out var existingCts))
                        {
                            await existingCts.CancelAsync();
                            _storageFlowTasks.Remove(flowId);
                        }

                        // Start new task
                        await StartStorageFlowAsync(flow, cancellationToken);
                    }
                    else
                    {
                        // Flow not found or inactive, remove it
                        await RemoveStorageFlowAsync(flowId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error refreshing storage flow {flowId}");
                }
            }
        }

        private async Task ListenToStorageFlowRemoveEventsAsync(CancellationToken cancellationToken)
        {
            await foreach (var flowId in _storageFlowRemoveChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await RemoveStorageFlowTaskAsync(flowId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error removing storage flow {flowId}");
                }
            }
        }

        private async Task ListenToRefreshAllEventsAsync(CancellationToken cancellationToken)
        {
            await foreach (var _ in _refreshAllChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    _logger.LogInformation("Refreshing all devices and storage flows...");
                    await InitialLoadAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing all");
                }
            }
        }

        private async Task StartDevicePollingAsync(Device device, CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Starting polling for device: {device.Name} ({device.Id})");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _deviceTasks[device.Id] = cts;

            // Start device polling in background
            _ = Task.Run(() => PollDeviceAsync(device, cts.Token), stoppingToken);
        }

        private async Task RemoveDevicePollingAsync(Guid deviceId)
        {
            if (_deviceTasks.TryGetValue(deviceId, out var cts))
            {
                _logger.LogInformation($"Stopping polling for device: {deviceId}");
                await cts.CancelAsync();
                _deviceTasks.Remove(deviceId);
            }
        }

        private async Task StartStorageFlowAsync(StorageFlow flow, CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Starting storage flow: {flow.Name} ({flow.Id})");
            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _storageFlowTasks[flow.Id] = cts;

            // Start storage flow in background
            _ = Task.Run(() => ExecuteStorageFlowAsync(flow, cts.Token), stoppingToken);
        }

        private async Task RemoveStorageFlowTaskAsync(Guid flowId)
        {
            if (_storageFlowTasks.TryGetValue(flowId, out var cts))
            {
                _logger.LogInformation($"Stopping storage flow: {flowId}");
                await cts.CancelAsync();
                _storageFlowTasks.Remove(flowId);
            }
        }

        private async Task LoadStorageFlowsAsync(AppDbContext dbContext, CancellationToken stoppingToken)
        {
            var activeFlows = await dbContext.StorageFlows
                .Where(sf => sf.IsActive && sf.DeletedAt == null)
                .Include(sf => sf.MasterTable)
                    .ThenInclude(mt => mt.Fields.Where(f => f.DeletedAt == null && f.IsEnabled))
                .Include(sf => sf.StorageFlowDevices)
                    .ThenInclude(sfd => sfd.Device)
                .Include(sf => sf.StorageFlowMappings)
                    .ThenInclude(sfm => sfm.MasterTableField)
                .Include(sf => sf.StorageFlowMappings)
                    .ThenInclude(sfm => sfm.Tag)
                .ToListAsync(stoppingToken);

            foreach (var flow in activeFlows)
            {
                await StartStorageFlowAsync(flow, stoppingToken);
            }
        }

        private async Task ExecuteStorageFlowAsync(StorageFlow flow, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Storage flow '{flow.Name}' started with interval {flow.StorageInterval}ms");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Reload flow with fresh data
                    var currentFlow = await dbContext.StorageFlows
                        .Where(sf => sf.Id == flow.Id && sf.IsActive && sf.DeletedAt == null)
                        .Include(sf => sf.MasterTable)
                            .ThenInclude(mt => mt.Fields.Where(f => f.DeletedAt == null && f.IsEnabled))
                        .Include(sf => sf.StorageFlowDevices)
                            .ThenInclude(sfd => sfd.Device)
                        .Include(sf => sf.StorageFlowMappings)
                            .ThenInclude(sfm => sfm.MasterTableField)
                        .Include(sf => sf.StorageFlowMappings)
                            .ThenInclude(sfm => sfm.Tag)
                                .ThenInclude(t => t!.Device)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (currentFlow == null)
                    {
                        _logger.LogWarning($"Storage flow {flow.Id} not found or no longer active");
                        break;
                    }

                    // Process each device in the flow
                    foreach (var flowDevice in currentFlow.StorageFlowDevices)
                    {
                        if (flowDevice.Device?.IsEnabled == true)
                        {
                            await ProcessDeviceDataAsync(currentFlow, flowDevice.Device, dbContext, cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error executing storage flow '{flow.Name}'");
                }

                // Wait for the storage interval
                await Task.Delay(flow.StorageInterval, cancellationToken);
            }

            _logger.LogInformation($"Storage flow '{flow.Name}' stopped");
        }

        private async Task ProcessDeviceDataAsync(StorageFlow flow, Device device, AppDbContext dbContext, CancellationToken _)
        {
            try
            {
                _logger.LogInformation($"[StorageFlow:{flow.Name}] Processing device: {device.Name}");

                // Get device data based on protocol
                object? responseData = await GetDeviceDataAsync(device);

                if (responseData == null)
                {
                    _logger.LogWarning($"[StorageFlow:{flow.Name}] No data received from device {device.Name}");
                    return;
                }

                _logger.LogInformation($"[StorageFlow:{flow.Name}] Device data received: {JsonSerializer.Serialize(responseData)}");

                // Extract and map data according to flow mappings
                var mappedData = ExtractMappedData(flow, device, responseData);

                if (mappedData.Count == 0)
                {
                    _logger.LogWarning($"[StorageFlow:{flow.Name}] No data could be mapped for device {device.Name} - Check your source_path mappings!");
                    return;
                }

                _logger.LogInformation($"[StorageFlow:{flow.Name}] Mapped data: {JsonSerializer.Serialize(mappedData)}");

                // Insert data into physical table
                await InsertDataIntoTableAsync(flow.MasterTable.TableName, mappedData, dbContext);

                _logger.LogInformation($"[StorageFlow:{flow.Name}] ✅ Data stored from device {device.Name} to table {flow.MasterTable.TableName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing device data for {device.Name} in flow {flow.Name}");
            }
        }

        private async Task<object?> GetDeviceDataAsync(Device device)
        {
            try
            {
                return device.Protocol switch
                {
                    Protocol.HTTP => await GetHttpDataAsync(device),
                    Protocol.MQTT => await GetMqttDataAsync(device),
                    Protocol.MODBUS_TCP or Protocol.MODBUS_RTU => await GetModbusDataAsync(device),
                    Protocol.OPC_UA => await GetOpcUaDataAsync(device),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting data from device {device.Name}");
                return null;
            }
        }

        private async Task<object?> GetHttpDataAsync(Device device)
        {
            var config = device.GetConfig<HttpConfig>();
            if (config == null) return null;

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

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
                _ => throw new InvalidOperationException($"Unsupported HTTP method: {config.Method}")
            };

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<object>(json);
        }

        private Task<object?> GetMqttDataAsync(Device device)
        {
            var config = device.GetConfig<MqttConfig>();
            if (config == null) return Task.FromResult<object?>(null);

            // Simulated MQTT data
            var random = new Random();
            var data = new
            {
                topic = config.Topic,
                temperature = Math.Round(20 + random.NextDouble() * 15, 2),
                humidity = Math.Round(40 + random.NextDouble() * 40, 2),
                pressure = Math.Round(1000 + random.NextDouble() * 50, 2)
            };

            return Task.FromResult<object?>(data);
        }

        private async Task<object?> GetModbusDataAsync(Device device)
        {
            // Get tags for this device
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tags = await dbContext.Tags
                .Where(t => t.DeviceId == device.Id && t.DeletedAt == null)
                .ToListAsync();

            // Simulate reading tag values
            var data = new Dictionary<string, object>();
            var random = new Random();

            foreach (var tag in tags)
            {
                // Simulate tag value based on data type
                object value = tag.DataType switch
                {
                    DataType.INT16 or DataType.INT32 => random.Next(0, 100),
                    DataType.UINT16 or DataType.UINT32 => random.Next(0, 100),
                    DataType.FLOAT => Math.Round(random.NextDouble() * 100, 2),
                    DataType.BOOLEAN => random.Next(0, 2) == 1,
                    DataType.STRING => $"Value_{random.Next(1, 10)}",
                    _ => 0
                };

                // Apply scaling if enabled
                if (tag.IsScaled && tag.DataType == DataType.FLOAT && 
                    tag.RawMin.HasValue && tag.RawMax.HasValue && 
                    tag.EuMin.HasValue && tag.EuMax.HasValue)
                {
                    var rawValue = (double)value;
                    value = tag.EuMin.Value + 
                        (rawValue - tag.RawMin.Value) * (tag.EuMax.Value - tag.EuMin.Value) / 
                        (tag.RawMax.Value - tag.RawMin.Value);
                }

                data[tag.Name] = value;
            }

            return data;
        }

        private async Task<object?> GetOpcUaDataAsync(Device device)
        {
            // Similar to Modbus, use tags
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tags = await dbContext.Tags
                .Where(t => t.DeviceId == device.Id && t.DeletedAt == null)
                .ToListAsync();

            var data = new Dictionary<string, object>();
            var random = new Random();

            foreach (var tag in tags)
            {
                object value = tag.DataType switch
                {
                    DataType.INT16 or DataType.INT32 => random.Next(0, 100),
                    DataType.UINT16 or DataType.UINT32 => random.Next(0, 100),
                    DataType.FLOAT => Math.Round(random.NextDouble() * 100, 2),
                    DataType.BOOLEAN => random.Next(0, 2) == 1,
                    DataType.STRING => $"Value_{random.Next(1, 10)}",
                    _ => 0
                };

                data[tag.Name] = value;
            }

            return data;
        }

        private Dictionary<string, object> ExtractMappedData(StorageFlow flow, Device device, object responseData)
        {
            var mappedData = new Dictionary<string, object>();

            try
            {
                // Convert response to JSON string for JSONPath processing
                var jsonString = JsonSerializer.Serialize(responseData);
                var jToken = JToken.Parse(jsonString);

                _logger.LogDebug($"[ExtractData] Processing {flow.StorageFlowMappings.Count} mappings for device {device.Name}");

                foreach (var mapping in flow.StorageFlowMappings)
                {
                    try
                    {
                        object? value = null;

                        // For HTTP/MQTT, use JSONPath
                        if (device.Protocol == Protocol.HTTP || device.Protocol == Protocol.MQTT)
                        {
                            _logger.LogDebug($"[ExtractData] Trying JSONPath: {mapping.SourcePath} -> Field: {mapping.MasterTableField.Name}");

                            // Use SelectToken for JSONPath
                            var token = jToken.SelectToken(mapping.SourcePath);
                            if (token != null)
                            {
                                value = token.ToObject<object>();
                                _logger.LogDebug($"[ExtractData] ✅ Extracted value: {value}");
                            }
                            else
                            {
                                _logger.LogWarning($"[ExtractData] ❌ JSONPath '{mapping.SourcePath}' returned null - Check if path is correct!");
                            }
                        }
                        // For MODBUS/OPCUA, use Tag name from dictionary
                        else if (device.Protocol == Protocol.MODBUS_TCP || device.Protocol == Protocol.MODBUS_RTU || device.Protocol == Protocol.OPC_UA)
                        {
                            if (responseData is Dictionary<string, object> dict)
                            {
                                dict.TryGetValue(mapping.SourcePath, out value);
                            }
                        }

                        if (value != null)
                        {
                            mappedData[mapping.MasterTableField.Name] = value;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"[ExtractData] Failed to extract data for path '{mapping.SourcePath}'");
                    }
                }

                _logger.LogInformation($"[ExtractData] Successfully mapped {mappedData.Count} fields out of {flow.StorageFlowMappings.Count} mappings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting mapped data");
            }

            return mappedData;
        }

        private async Task InsertDataIntoTableAsync(string tableName, Dictionary<string, object> data, AppDbContext dbContext)
        {
            if (data.Count == 0) return;

            try
            {
                // Build INSERT statement
                var columns = new List<string> { "`Id`", "`CreatedAt`" };
                var values = new List<string> { $"'{Guid.NewGuid()}'", "NOW()" };

                foreach (var kvp in data)
                {
                    columns.Add($"`{kvp.Key}`");
                    values.Add(FormatValueForSql(kvp.Value));
                }

                var insertSql = $"INSERT INTO `{tableName}` ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";

                await dbContext.Database.ExecuteSqlRawAsync(insertSql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error inserting data into table {tableName}");
                throw;
            }
        }

        private static string FormatValueForSql(object value)
        {
            if (value == null) return "NULL";

            return value switch
            {
                string s => $"'{s.Replace("'", "''")}'" ,
                bool b => b ? "1" : "0",
                DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
                int or long or short or byte => value.ToString()!,
                float or double or decimal => value.ToString()!.Replace(",", "."),
                _ => $"'{value.ToString()!.Replace("'", "''")}'"
            };
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
                        await _hubContext.Clients.Group($"{_deviceGroupPrefix}{device.Id}")
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

            // Cancel all storage flow tasks
            foreach (var cts in _storageFlowTasks.Values)
            {
                await cts.CancelAsync();
            }

            _storageFlowTasks.Clear();

            await base.StopAsync(cancellationToken);
        }
    }
}
