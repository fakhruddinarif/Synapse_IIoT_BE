using Core.DTOs;
using Core.DTOs.Device;
using Core.Entities;
using Core.Enums;
using Core.Acquisition;
using Core.Interface;
using Core.Security;
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
        private readonly IAcquisitionControl _acquisition;
        private readonly ITagEngine _tagEngine;
        private readonly string _deviceGroupPrefix;
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
            IConfiguration configuration,
            IAcquisitionControl acquisition,
            ITagEngine tagEngine)
        {
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
            _acquisition = acquisition;
            _tagEngine = tagEngine;
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

                // Penarikan data perangkat BUKAN lagi tugas service ini — AcquisitionWorker
                // yang memilikinya, lengkap dengan kelas scan, penilaian quality, dan
                // buffer tahan-mati. Yang tersisa di sini adalah storage flow: memetakan
                // data ke tabel buatan pengguna pada interval tersendiri.
                _acquisition.RequestReload("initial load");

                // Load all active storage flows
                await LoadStorageFlowsAsync(dbContext, cancellationToken);

                _logger.LogInformation($"Initial load completed: {_storageFlowTasks.Count} storage flows");
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
                    // Tidak perlu memeriksa perangkatnya di sini: penjadwal membaca ulang
                    // SELURUH konfigurasi aktif, jadi perangkat yang dimatikan, dihapus,
                    // maupun baru ditambahkan tertangani oleh satu jalur yang sama — tidak
                    // ada lagi cabang "kalau begini hentikan, kalau begitu mulai" yang bisa
                    // menyimpang dari keadaan sebenarnya.
                    _acquisition.RequestReload($"perangkat {deviceId} berubah");
                    await Task.CompletedTask;
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
                    _acquisition.RequestReload($"perangkat {deviceId} dihapus");
                    await Task.CompletedTask;
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
                    // HTTP tetap diambil langsung: pemetaannya memakai JSONPath atas
                    // payload asli, dan itu memang bekerja hari ini.
                    Protocol.HTTP => await GetHttpDataAsync(device),

                    // Sisanya memakai nilai yang sudah diakuisisi, dikunci nama tag.
                    _ => await GetTagValuesFromEngineAsync(device)
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

        /// <summary>
        /// Mengambil nilai tag terbaru dari basis data nilai-sekarang (RTDB) yang diisi
        /// AcquisitionWorker, dikunci berdasarkan NAMA TAG.
        ///
        /// Menggantikan tiga metode sebelumnya (MQTT, Modbus, OPC UA) yang masing-masing
        /// MENGARANG nilai dengan Random dan menuliskannya ke tabel pengguna sebagai riwayat
        /// pabrik. Angka acak yang tersimpan permanen jauh lebih berbahaya daripada tidak ada
        /// angka sama sekali: keduanya sama-sama tidak informatif, tetapi yang pertama
        /// dipercaya.
        ///
        /// Sekarang sumbernya satu-satunya adalah nilai yang benar-benar diakuisisi. Protokol
        /// yang belum punya driver menghasilkan kumpulan kosong — storage flow melewatkan
        /// siklusnya dan mencatat alasannya, bukan mengisi tabel dengan tebakan.
        /// </summary>
        private async Task<object?> GetTagValuesFromEngineAsync(Device device)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tags = await dbContext.Tags
                .AsNoTracking()
                .Where(t => t.DeviceId == device.Id && t.DeletedAt == null && t.IsActive)
                .Select(t => new { t.Id, t.Name })
                .ToListAsync();

            var data = new Dictionary<string, object>();

            foreach (var tag in tags)
            {
                var snapshot = _tagEngine.GetSnapshot(tag.Id);
                if (snapshot is null) continue;

                // Quality Bad berarti nilainya tidak diketahui. Menyimpannya sebagai angka
                // biasa menghapus perbedaan antara "nilai ini benar" dan "kami kehilangan
                // kontak" — dan justru perbedaan itu yang dicari saat hasil produksi
                // ditelusuri kembali.
                if (snapshot.Sample.Quality == Quality.Bad) continue;

                object? value = snapshot.Sample.Numeric
                    ?? (object?)snapshot.Sample.Boolean
                    ?? snapshot.Sample.Text;

                if (value is not null) data[tag.Name] = value;
            }

            if (data.Count == 0)
            {
                _logger.LogWarning(
                    "Tidak ada nilai tag terbaru untuk {Device} ({Protocol}); storage flow tidak menulis apa pun siklus ini",
                    device.Name, device.Protocol);
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

                        // Hanya HTTP yang punya payload JSON asli untuk ditelusuri.
                        if (device.Protocol == Protocol.HTTP)
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
                        // Protokol non-HTTP: kuncinya nama tag, bukan JSONPath.
                        else if (responseData is Dictionary<string, object> dict)
                        {
                            if (!dict.TryGetValue(mapping.SourcePath, out value))
                            {
                                // Toleransi konfigurasi lama yang menyimpan "$.nama":
                                // dulu MQTT dipetakan dengan JSONPath atas payload
                                // karangan. Prefix dilepas dan sisanya dicoba sebagai nama
                                // tag, supaya pemetaan yang sudah ada tidak mati diam-diam.
                                var bare = mapping.SourcePath.StartsWith("$.")
                                    ? mapping.SourcePath[2..]
                                    : mapping.SourcePath;

                                dict.TryGetValue(bare, out value);
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

        /// <summary>
        /// Menulis satu baris ke tabel dinamis, SELALU dengan parameter — tidak pernah dengan
        /// nilai yang dirangkai ke dalam teks SQL.
        ///
        /// Sebelumnya nilai dari perangkat (termasuk teks bebas dari payload HTTP) dirangkai
        /// langsung menjadi literal SQL lewat `FormatValueForSql`, dengan eskapnya ditulis
        /// tangan (`Replace("'", "''")`) untuk setiap tipe. Itu jalur injeksi yang nyata: nama
        /// kolom berasal dari tabel yang pengguna definisikan sendiri, tapi ISI baris berasal
        /// dari perangkat di lapangan — dan perangkat yang disusupi atau firmware yang ngawur
        /// bisa mengirim apa saja. Nama kolom (dan nama tabel) tetap tidak bisa diparameterkan
        /// oleh SQL apa pun, jadi keduanya melewati <see cref="SqlIdentifier.EnsureSafe"/> tepat
        /// di titik ini; nilai barisnya sepenuhnya lewat parameter, memakai mekanisme placeholder
        /// <c>{0}</c> yang sama seperti kueri lain di codebase ini (lihat
        /// <c>StorageFlowService.CreatePhysicalTableAsync</c>).
        /// </summary>
        private async Task InsertDataIntoTableAsync(string tableName, Dictionary<string, object> data, AppDbContext dbContext)
        {
            if (data.Count == 0) return;

            try
            {
                var safeTable = SqlIdentifier.EnsureSafe(tableName, "tabel");
                var columns = new List<string> { "\"id\"", "\"created_at\"" };
                var placeholders = new List<string> { "{0}", "{1}" };
                var parameters = new List<object> { Guid.NewGuid(), DateTime.UtcNow };

                foreach (var (key, value) in data)
                {
                    var safeColumn = SqlIdentifier.EnsureSafe(key, "kolom");
                    columns.Add($"\"{safeColumn}\"");
                    placeholders.Add($"{{{parameters.Count}}}");
                    parameters.Add(value ?? DBNull.Value);
                }

                var insertSql =
                    $"INSERT INTO \"{safeTable}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";

                await dbContext.Database.ExecuteSqlRawAsync(insertSql, parameters.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error inserting data into table {tableName}");
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("DeviceWorkerService stopping...");

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
