using Core.DTOs;
using Core.DTOs.Device;
using Core.Entities;
using Core.Enums;
using Core.Interface;
using System.Text.Json;

namespace Infrastructure.Services
{
	public class DeviceService : IDeviceService
	{
		private readonly IDeviceRepository _deviceRepository;
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly IDeviceWorkerService _deviceWorkerService;
		private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

		public DeviceService(
			IDeviceRepository deviceRepository, 
			IHttpClientFactory httpClientFactory,
			IDeviceWorkerService deviceWorkerService)
		{
			_deviceRepository = deviceRepository;
			_httpClientFactory = httpClientFactory;
			_deviceWorkerService = deviceWorkerService;
		}

		public async Task<ApiResponse<DeviceResponseDto>> GetByIdAsync(Guid id)
		{
			var device = await _deviceRepository.GetByIdAsync(id);

			if (device == null)
			{
				return ApiResponse<DeviceResponseDto>.Fail(404, "Device not found");
			}

			return ApiResponse<DeviceResponseDto>.Success(DeviceResponseDto.FromEntity(device));
		}

		public async Task<ApiResponse<List<DeviceResponseDto>>> GetAllAsync(DeviceFilterDto filter)
		{
			var (devices, totalCount) = await _deviceRepository.GetAllAsync(filter);

			var deviceDtos = devices.Select(DeviceResponseDto.FromEntity).ToList();

			var pagingInfo = new PagingInfo
			{
				Page = filter.Page,
				Size = filter.PageSize,
				TotalItem = totalCount,
				TotalPage = (int)Math.Ceiling(totalCount / (double)filter.PageSize)
			};

			return ApiResponse<List<DeviceResponseDto>>.Success(deviceDtos, "Devices retrieved successfully", pagingInfo);
		}

		public async Task<ApiResponse<DeviceResponseDto>> CreateAsync(CreateDeviceDto dto)
		{
			// Validate device name uniqueness
			if (await _deviceRepository.NameExistsAsync(dto.Name))
			{
				return ApiResponse<DeviceResponseDto>.Fail(400, "Device name already exists", new { name = "Device name must be unique" });
			}

			// Validate and serialize connection config based on protocol
			string configJson;
			try
			{
				configJson = dto.Protocol switch
				{
					Protocol.HTTP => ValidateAndSerializeConfig<HttpConfig>(dto.ConnectionConfig),
					Protocol.MODBUS_TCP => ValidateAndSerializeConfig<ModbusTcpConfig>(dto.ConnectionConfig),
					Protocol.MODBUS_RTU => ValidateAndSerializeConfig<ModbusRtuConfig>(dto.ConnectionConfig),
					Protocol.MQTT => ValidateAndSerializeConfig<MqttConfig>(dto.ConnectionConfig),
					Protocol.OPC_UA => ValidateAndSerializeConfig<OpcUaConfig>(dto.ConnectionConfig),
					_ => "{}"
				};
			}
			catch (Exception ex)
			{
				return ApiResponse<DeviceResponseDto>.Fail(400, "Invalid connection configuration", new { config = ex.Message });
			}

			var device = new Device
			{
				Name = dto.Name,
				Description = dto.Description,
				IsEnabled = dto.IsEnabled,
				Protocol = dto.Protocol,
				ConnectionConfigJson = configJson,
				PollingInterval = dto.PollingInterval
			};

			var createdDevice = await _deviceRepository.CreateAsync(device);

			// Trigger event-driven update
			await _deviceWorkerService.RefreshDeviceAsync(createdDevice.Id);

			return ApiResponse<DeviceResponseDto>.SuccessWithStatus(201, DeviceResponseDto.FromEntity(createdDevice), "Device created successfully");
		}

		public async Task<ApiResponse<DeviceResponseDto>> UpdateAsync(Guid id, UpdateDeviceDto dto)
		{
			var device = await _deviceRepository.GetByIdAsync(id);

			if (device == null)
			{
				return ApiResponse<DeviceResponseDto>.Fail(404, "Device not found");
			}

			// Check name uniqueness if name is being updated
			if (dto.Name != null && dto.Name != device.Name)
			{
				if (await _deviceRepository.NameExistsAsync(dto.Name, id))
				{
					return ApiResponse<DeviceResponseDto>.Fail(400, "Device name already exists", new { name = "Device name must be unique" });
				}
				device.Name = dto.Name;
			}

			// Update other fields
			if (dto.Description != null)
			{
				device.Description = dto.Description;
			}

			if (dto.IsEnabled.HasValue)
			{
				device.IsEnabled = dto.IsEnabled.Value;
			}

			if (dto.PollingInterval.HasValue)
			{
				device.PollingInterval = dto.PollingInterval.Value;
			}

			// Update protocol and config if provided
			if (dto.Protocol.HasValue && dto.ConnectionConfig != null)
			{
				try
				{
					device.Protocol = dto.Protocol.Value;
					device.ConnectionConfigJson = dto.Protocol.Value switch
					{
						Protocol.HTTP => ValidateAndSerializeConfig<HttpConfig>(dto.ConnectionConfig),
						Protocol.MODBUS_TCP => ValidateAndSerializeConfig<ModbusTcpConfig>(dto.ConnectionConfig),
						Protocol.MODBUS_RTU => ValidateAndSerializeConfig<ModbusRtuConfig>(dto.ConnectionConfig),
						Protocol.MQTT => ValidateAndSerializeConfig<MqttConfig>(dto.ConnectionConfig),
						Protocol.OPC_UA => ValidateAndSerializeConfig<OpcUaConfig>(dto.ConnectionConfig),
						_ => "{}"
					};
				}
				catch (Exception ex)
				{
					return ApiResponse<DeviceResponseDto>.Fail(400, "Invalid connection configuration", new { config = ex.Message });
				}
			}
			else if (dto.ConnectionConfig != null)
			{
				// Update config only, keep existing protocol
				try
				{
					device.ConnectionConfigJson = device.Protocol switch
					{
						Protocol.HTTP => ValidateAndSerializeConfig<HttpConfig>(dto.ConnectionConfig),
						Protocol.MODBUS_TCP => ValidateAndSerializeConfig<ModbusTcpConfig>(dto.ConnectionConfig),
						Protocol.MODBUS_RTU => ValidateAndSerializeConfig<ModbusRtuConfig>(dto.ConnectionConfig),
						Protocol.MQTT => ValidateAndSerializeConfig<MqttConfig>(dto.ConnectionConfig),
						Protocol.OPC_UA => ValidateAndSerializeConfig<OpcUaConfig>(dto.ConnectionConfig),
						_ => "{}"
					};
				}
				catch (Exception ex)
				{
					return ApiResponse<DeviceResponseDto>.Fail(400, "Invalid connection configuration", new { config = ex.Message });
				}
			}

			var updatedDevice = await _deviceRepository.UpdateAsync(device);

			// Trigger event-driven update
			await _deviceWorkerService.RefreshDeviceAsync(updatedDevice!.Id);

			return ApiResponse<DeviceResponseDto>.Success(DeviceResponseDto.FromEntity(updatedDevice!), "Device updated successfully");
		}

		public async Task<ApiResponse<object>> DeleteAsync(Guid id)
		{
			var exists = await _deviceRepository.ExistsAsync(id);

			if (!exists)
			{
				return ApiResponse<object>.Fail(404, "Device not found");
			}

			await _deviceRepository.DeleteAsync(id);

			// Trigger event-driven removal
			await _deviceWorkerService.RemoveDeviceAsync(id);

			return ApiResponse<object>.Success(null, "Device deleted successfully");
		}

		public async Task<ApiResponse<TestHttpConnectionResponseDto>> TestHttpConnectionAsync(TestHttpRequestDto request)
		{
			try
			{
				// Validate URL
				if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out _))
				{
					return ApiResponse<TestHttpConnectionResponseDto>.Fail(400, "Invalid URL provided");
				}

				var client = _httpClientFactory.CreateClient();
				client.Timeout = TimeSpan.FromSeconds(30);

				// Add headers if provided
				if (request.Headers != null)
				{
					foreach (var header in request.Headers)
					{
						client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
					}
				}

				// Make request based on method
				HttpResponseMessage response;
				var method = request.Method?.ToUpper() ?? "GET";

				switch (method)
				{
					case "GET":
						response = await client.GetAsync(request.Url);
						break;
					case "POST":
						var content = new StringContent(
							request.Body ?? string.Empty,
							System.Text.Encoding.UTF8,
							"application/json");
						response = await client.PostAsync(request.Url, content);
						break;
					default:
						return ApiResponse<TestHttpConnectionResponseDto>.Fail(400, "Unsupported HTTP method. Use GET or POST");
				}

				var responseContent = await response.Content.ReadAsStringAsync();

				// Try to parse as JSON
				object? parsedData = null;
				try
				{
					parsedData = JsonSerializer.Deserialize<object>(responseContent, _jsonOptions);
				}
				catch
				{
					parsedData = responseContent; // Return as string if not JSON
				}

				var result = new TestHttpConnectionResponseDto
				{
					RequestUrl = request.Url,
					RequestMethod = method,
					ResponseStatusCode = (int)response.StatusCode,
					ResponseData = parsedData,
					ResponseHeaders = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
					IsSuccess = response.IsSuccessStatusCode,
					ErrorMessage = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
				};

				var message = response.IsSuccessStatusCode
					? "HTTP connection test successful"
					: $"HTTP connection returned status {(int)response.StatusCode}";

				return ApiResponse<TestHttpConnectionResponseDto>.Success(result, message);
			}
			catch (HttpRequestException ex)
			{
				var errorResult = new TestHttpConnectionResponseDto
				{
					RequestUrl = request.Url,
					RequestMethod = request.Method ?? "GET",
					IsSuccess = false,
					ErrorMessage = $"HTTP request failed: {ex.Message}"
				};
				return ApiResponse<TestHttpConnectionResponseDto>.Fail(400, "HTTP request failed", errorResult);
			}
			catch (TaskCanceledException)
			{
				var errorResult = new TestHttpConnectionResponseDto
				{
					RequestUrl = request.Url,
					RequestMethod = request.Method ?? "GET",
					IsSuccess = false,
					ErrorMessage = "Request timeout (30 seconds exceeded)"
				};
				return ApiResponse<TestHttpConnectionResponseDto>.Fail(408, "Request timeout", errorResult);
			}
			catch (Exception ex)
			{
				var errorResult = new TestHttpConnectionResponseDto
				{
					RequestUrl = request.Url,
					RequestMethod = request.Method ?? "GET",
					IsSuccess = false,
					ErrorMessage = ex.Message
				};
				return ApiResponse<TestHttpConnectionResponseDto>.Fail(500, "An error occurred while testing HTTP connection", errorResult);
			}
		}

		private static string ValidateAndSerializeConfig<T>(object config)
		{
			// Serialize the object to JSON string first
			var jsonString = JsonSerializer.Serialize(config, _jsonOptions);

			// Deserialize to the target type to validate structure
			var typedConfig = JsonSerializer.Deserialize<T>(jsonString, _jsonOptions);

			if (EqualityComparer<T>.Default.Equals(typedConfig, default))
			{
				throw new ArgumentException($"Invalid configuration for type {typeof(T).Name}");
			}

			// Serialize back to ensure clean JSON
			return JsonSerializer.Serialize(typedConfig, _jsonOptions);
		}
	}
}
