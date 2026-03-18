using Core.DTOs;
using Core.DTOs.StorageFlow;
using Core.Entities;
using Core.Enums;
using Core.Exceptions;
using Core.Interface;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Services
{
    public class StorageFlowService : IStorageFlowService
    {
        private readonly IStorageFlowRepository _storageFlowRepository;
        private readonly IMasterTableRepository _masterTableRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDeviceWorkerService _deviceWorkerService;

        public StorageFlowService(
            IStorageFlowRepository storageFlowRepository,
            IMasterTableRepository masterTableRepository,
            IDeviceRepository deviceRepository,
            AppDbContext context,
            IHttpClientFactory httpClientFactory,
            IDeviceWorkerService deviceWorkerService)
        {
            _storageFlowRepository = storageFlowRepository;
            _masterTableRepository = masterTableRepository;
            _deviceRepository = deviceRepository;
            _context = context;
            _httpClientFactory = httpClientFactory;
            _deviceWorkerService = deviceWorkerService;
        }

        public async Task<StorageFlowResponseDto> GetByIdAsync(Guid id)
        {
            var flow = await _storageFlowRepository.GetByIdAsync(id, includeRelations: true);

            if (flow == null)
            {
                throw new NotFoundException("Storage flow not found");
            }

            return MapToResponseDto(flow);
        }

        public async Task<IEnumerable<StorageFlowResponseDto>> GetAllAsync()
        {
            var flows = await _storageFlowRepository.GetAllAsync(includeDeleted: false, includeRelations: true);
            return flows.Select(MapToResponseDto);
        }

        public async Task<StorageFlowResponseDto> CreateAsync(CreateStorageFlowDto dto)
        {
            // Validate MasterTable exists
            var masterTable = await _context.MasterTables
                .Include(mt => mt.Fields)
                .Where(mt => mt.Id == dto.MasterTableId && mt.DeletedAt == null)
                .FirstOrDefaultAsync();

            if (masterTable == null)
            {
                throw new NotFoundException("Master table not found");
            }

            // Validate devices exist
            var devices = new List<Device>();
            foreach (var deviceId in dto.DeviceIds)
            {
                var device = await _context.Devices
                    .Where(d => d.Id == deviceId && d.DeletedAt == null)
                    .FirstOrDefaultAsync();
                
                if (device == null)
                {
                    throw new BadRequestException($"Device with ID {deviceId} not found");
                }
                
                devices.Add(device);
            }

            // Validate fields exist
            var fields = new List<MasterTableFields>();
            foreach (var mapping in dto.Mappings)
            {
                var field = await _context.MasterTableFields
                    .Where(f => f.Id == mapping.MasterTableFieldId && f.MasterTableId == dto.MasterTableId && f.DeletedAt == null)
                    .FirstOrDefaultAsync();
                
                if (field == null)
                {
                    throw new BadRequestException($"Master table field with ID {mapping.MasterTableFieldId} not found");
                }
                
                if (!fields.Any(f => f.Id == field.Id))
                {
                    fields.Add(field);
                }
            }

            // Validate tags if specified
            var mappingsWithTags = dto.Mappings.Where(m => m.TagId.HasValue).ToList();
            foreach (var mapping in mappingsWithTags)
            {
                var tag = await _context.Tags
                    .Where(t => t.Id == mapping.TagId!.Value && t.DeletedAt == null)
                    .FirstOrDefaultAsync();
                
                if (tag == null)
                {
                    throw new BadRequestException($"Tag with ID {mapping.TagId} not found");
                }
            }

            // Create StorageFlow
            var storageFlow = new StorageFlow
            {
                Name = dto.Name,
                Description = dto.Description,
                IsActive = dto.IsActive,
                StorageInterval = dto.StorageInterval,
                MasterTableId = dto.MasterTableId
            };

            storageFlow = await _storageFlowRepository.CreateAsync(storageFlow);

            // Create StorageFlowDevices
            foreach (var deviceId in dto.DeviceIds)
            {
                var flowDevice = new StorageFlowDevice
                {
                    Id = Guid.NewGuid(),
                    StorageFlowId = storageFlow.Id,
                    DeviceId = deviceId
                };
                await _context.StorageFlowDevices.AddAsync(flowDevice);
            }

            // Create StorageFlowMappings
            foreach (var mappingDto in dto.Mappings)
            {
                var mapping = new StorageFlowMapping
                {
                    Id = Guid.NewGuid(),
                    StorageFlowId = storageFlow.Id,
                    MasterTableFieldId = mappingDto.MasterTableFieldId,
                    SourcePath = mappingDto.SourcePath,
                    TagId = mappingDto.TagId
                };
                await _context.StorageFlowMappings.AddAsync(mapping);
            }

            await _context.SaveChangesAsync();

            // Create physical table if MasterTable is active
            if (masterTable.IsActive)
            {
                await CreatePhysicalTableAsync(masterTable);
            }

            // Trigger event-driven update
            await _deviceWorkerService.RefreshStorageFlowAsync(storageFlow.Id);

            return await GetByIdAsync(storageFlow.Id);
        }

        public async Task<StorageFlowResponseDto> UpdateAsync(Guid id, UpdateStorageFlowDto dto)
        {
            var existingFlow = await _storageFlowRepository.GetByIdAsync(id, includeRelations: true);

            if (existingFlow == null)
            {
                throw new NotFoundException("Storage flow not found");
            }

            // Update basic properties
            if (!string.IsNullOrEmpty(dto.Name))
                existingFlow.Name = dto.Name;

            if (dto.Description != null)
                existingFlow.Description = dto.Description;

            if (dto.IsActive.HasValue)
                existingFlow.IsActive = dto.IsActive.Value;

            if (dto.StorageInterval.HasValue)
                existingFlow.StorageInterval = dto.StorageInterval.Value;

            if (dto.MasterTableId.HasValue)
            {
                var masterTable = await _masterTableRepository.GetByIdAsync(dto.MasterTableId.Value);
                if (masterTable == null)
                {
                    throw new NotFoundException("Master table not found");
                }
                existingFlow.MasterTableId = dto.MasterTableId.Value;
            }

            // Update devices if provided
            if (dto.DeviceIds != null)
            {
                // Remove existing device associations
                var existingDevices = await _context.StorageFlowDevices
                    .Where(sfd => sfd.StorageFlowId == id)
                    .ToListAsync();
                _context.StorageFlowDevices.RemoveRange(existingDevices);

                // Add new device associations
                foreach (var deviceId in dto.DeviceIds)
                {
                    var device = await _deviceRepository.GetByIdAsync(deviceId);
                    if (device == null)
                    {
                        throw new BadRequestException($"Device with ID {deviceId} not found");
                    }

                    var flowDevice = new StorageFlowDevice
                    {
                        Id = Guid.NewGuid(),
                        StorageFlowId = id,
                        DeviceId = deviceId
                    };
                    await _context.StorageFlowDevices.AddAsync(flowDevice);
                }
            }

            // Update mappings if provided
            if (dto.Mappings != null)
            {
                // Remove existing mappings
                var existingMappings = await _context.StorageFlowMappings
                    .Where(sfm => sfm.StorageFlowId == id)
                    .ToListAsync();
                _context.StorageFlowMappings.RemoveRange(existingMappings);

                // Add new mappings
                foreach (var mappingDto in dto.Mappings)
                {
                    var mapping = new StorageFlowMapping
                    {
                        Id = Guid.NewGuid(),
                        StorageFlowId = id,
                        MasterTableFieldId = mappingDto.MasterTableFieldId,
                        SourcePath = mappingDto.SourcePath,
                        TagId = mappingDto.TagId
                    };
                    await _context.StorageFlowMappings.AddAsync(mapping);
                }
            }

            await _storageFlowRepository.UpdateAsync(existingFlow);
            await _context.SaveChangesAsync();

            // Trigger event-driven update
            await _deviceWorkerService.RefreshStorageFlowAsync(id);

            return await GetByIdAsync(id);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var result = await _storageFlowRepository.DeleteAsync(id);
            
            // Trigger event-driven removal
            if (result)
            {
                await _deviceWorkerService.RemoveStorageFlowAsync(id);
            }
            
            return result;
        }

        public async Task<List<DiscoveredFieldDto>> DiscoverFieldsAsync(Guid deviceId)
        {
            var device = await _deviceRepository.GetByIdAsync(deviceId);

            if (device == null)
            {
                throw new NotFoundException("Device not found");
            }

            if (!device.IsEnabled)
            {
                throw new BadRequestException("Device is not enabled. Please enable it first.");
            }

            var discoveredFields = new List<DiscoveredFieldDto>();

            switch (device.Protocol)
            {
                case Protocol.HTTP:
                case Protocol.MQTT:
                    discoveredFields = await DiscoverHttpMqttFieldsAsync(device);
                    break;

                case Protocol.MODBUS_TCP:
                case Protocol.MODBUS_RTU:
                case Protocol.OPC_UA:
                    discoveredFields = await DiscoverIndustrialFieldsAsync(device);
                    break;

                default:
                    throw new BadRequestException($"Protocol {device.Protocol} is not supported");
            }

            return discoveredFields;
        }

        #region Private Helper Methods

        private StorageFlowResponseDto MapToResponseDto(StorageFlow flow)
        {
            return new StorageFlowResponseDto
            {
                Id = flow.Id,
                Name = flow.Name,
                Description = flow.Description,
                IsActive = flow.IsActive,
                StorageInterval = flow.StorageInterval,
                MasterTableId = flow.MasterTableId,
                MasterTableName = flow.MasterTable?.Name ?? "",
                Devices = flow.StorageFlowDevices.Select(sfd => new StorageFlowDeviceDto
                {
                    DeviceId = sfd.Device.Id,
                    DeviceName = sfd.Device.Name,
                    Protocol = sfd.Device.Protocol.ToString(),
                    IsEnabled = sfd.Device.IsEnabled
                }).ToList(),
                Mappings = flow.StorageFlowMappings.Select(sfm => new StorageFlowMappingDto
                {
                    Id = sfm.Id,
                    MasterTableFieldId = sfm.MasterTableFieldId,
                    FieldName = sfm.MasterTableField?.Name ?? "",
                    FieldDataType = sfm.MasterTableField?.DataType.ToString() ?? "",
                    SourcePath = sfm.SourcePath,
                    TagId = sfm.TagId,
                    TagName = sfm.Tag?.Name
                }).ToList(),
                CreatedAt = flow.CreatedAt,
                UpdatedAt = flow.UpdatedAt
            };
        }

        private async Task CreatePhysicalTableAsync(MasterTable masterTable)
        {
            var tableName = masterTable.TableName;
            var fields = masterTable.Fields.Where(f => f.DeletedAt == null).ToList();

            if (!fields.Any())
            {
                return;
            }

            // Check if table already exists
            var checkTableSql = "SELECT COUNT(*) as Value FROM information_schema.tables WHERE table_name = {0}";
            var tableExists = await _context.Database
                .SqlQueryRaw<int>(checkTableSql, tableName)
                .FirstOrDefaultAsync();

            if (tableExists > 0)
            {
                return; // Table already exists
            }

            // Build CREATE TABLE SQL
            var columnDefinitions = new List<string>
            {
                "`Id` CHAR(36) PRIMARY KEY",
                "`CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP"
            };

            foreach (var field in fields)
            {
                var sqlType = MapDataTypeToSql(field.DataType);
                columnDefinitions.Add($"`{field.Name}` {sqlType}");
            }

            var createTableSql = $"CREATE TABLE `{tableName}` ({string.Join(", ", columnDefinitions)})";

            await _context.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private static string MapDataTypeToSql(DataTypeTable dataType)
        {
            return dataType switch
            {
                DataTypeTable.STRING => "VARCHAR(255)",
                DataTypeTable.INTEGER => "INT",
                DataTypeTable.FLOAT => "DOUBLE",
                DataTypeTable.BOOLEAN => "BOOLEAN",
                DataTypeTable.DATETIME => "DATETIME",
                _ => "VARCHAR(255)"
            };
        }

        private async Task<List<DiscoveredFieldDto>> DiscoverHttpMqttFieldsAsync(Device device)
        {
            try
            {
                object? responseData = null;

                if (device.Protocol == Protocol.HTTP)
                {
                    var config = device.GetConfig<HttpConfig>();
                    if (config == null)
                    {
                        throw new BadRequestException("Invalid HTTP configuration");
                    }

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
                        _ => throw new BadRequestException($"Unsupported HTTP method: {config.Method}")
                    };

                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();
                    responseData = JsonSerializer.Deserialize<object>(json);
                }
                else if (device.Protocol == Protocol.MQTT)
                {
                    // For MQTT, we'll use simulated data
                    responseData = new
                    {
                        topic = "sensor/data",
                        temperature = 25.5,
                        humidity = 60.0,
                        pressure = 1013.25
                    };
                }

                if (responseData == null)
                {
                    return new List<DiscoveredFieldDto>();
                }

                // Parse JSON and extract all fields with JSONPath
                var jsonString = JsonSerializer.Serialize(responseData);
                var jObject = JObject.Parse(jsonString);

                return ExtractFieldsFromJson(jObject, "$");
            }
            catch (Exception ex)
            {
                throw new BadRequestException($"Failed to discover fields: {ex.Message}");
            }
        }

        private List<DiscoveredFieldDto> ExtractFieldsFromJson(JToken token, string path)
        {
            var fields = new List<DiscoveredFieldDto>();

            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    var currentPath = $"{path}.{property.Name}";

                    if (property.Value is JObject || property.Value is JArray)
                    {
                        // Recurse into nested objects/arrays
                        fields.AddRange(ExtractFieldsFromJson(property.Value, currentPath));
                    }
                    else
                    {
                        // Leaf node - add as discovered field
                        fields.Add(new DiscoveredFieldDto
                        {
                            Path = currentPath,
                            Type = GetJsonValueType(property.Value),
                            SampleValue = property.Value.ToString()
                        });
                    }
                }
            }
            else if (token is JArray array)
            {
                // For arrays, show the structure of first element
                if (array.Count > 0)
                {
                    fields.AddRange(ExtractFieldsFromJson(array[0], $"{path}[0]"));
                }
            }

            return fields;
        }

        private static string GetJsonValueType(JToken token)
        {
            return token.Type switch
            {
                JTokenType.String => "STRING",
                JTokenType.Integer => "INTEGER",
                JTokenType.Float => "FLOAT",
                JTokenType.Boolean => "BOOLEAN",
                JTokenType.Date => "DATETIME",
                _ => "STRING"
            };
        }

        private async Task<List<DiscoveredFieldDto>> DiscoverIndustrialFieldsAsync(Device device)
        {
            // For MODBUS and OPC UA, discover fields from Tags
            var tags = await _context.Tags
                .Where(t => t.DeviceId == device.Id && t.DeletedAt == null)
                .ToListAsync();

            return tags.Select(tag => new DiscoveredFieldDto
            {
                Path = tag.Name,
                Type = tag.DataType.ToString(),
                SampleValue = $"{tag.Address} ({tag.AccessMode})"
            }).ToList();
        }

        #endregion
    }
}
