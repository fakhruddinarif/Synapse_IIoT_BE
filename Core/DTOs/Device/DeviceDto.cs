using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Core.Enums;

namespace Core.DTOs.Device
{
	public class CreateDeviceDto
	{
		[Required]
		[StringLength(100)]
		public string Name { get; set; } = string.Empty;

		[MaxLength(255)]
		public string? Description { get; set; }

		public bool IsEnabled { get; set; } = false;

		[Required]
		public Protocol Protocol { get; set; } = Protocol.HTTP;

		[Required]
		public object ConnectionConfig { get; set; } = new { };

		public int PollingInterval { get; set; } = 1000;
	}

	public class UpdateDeviceDto
	{
		[StringLength(100)]
		public string? Name { get; set; }

		[MaxLength(255)]
		public string? Description { get; set; }

		public bool? IsEnabled { get; set; }

		public Protocol? Protocol { get; set; }

		public object? ConnectionConfig { get; set; }

		public int? PollingInterval { get; set; }
	}

	public class DeviceResponseDto
	{
		public Guid Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public string? Description { get; set; }
		public bool IsEnabled { get; set; }
		public Protocol Protocol { get; set; }
		public object ConnectionConfig { get; set; } = new { };
		public int PollingInterval { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime? UpdatedAt { get; set; }

		public static DeviceResponseDto FromEntity(Core.Entities.Device device)
		{
			object config;

			try
			{
				// Deserialize based on protocol type
				config = device.Protocol switch
				{
					Protocol.HTTP => device.GetConfig<HttpConfig>() ?? new HttpConfig(),
					Protocol.MODBUS_TCP => device.GetConfig<ModbusTcpConfig>() ?? new ModbusTcpConfig(),
					Protocol.MODBUS_RTU => device.GetConfig<ModbusRtuConfig>() ?? new ModbusRtuConfig(),
					Protocol.MQTT => device.GetConfig<MqttConfig>() ?? new MqttConfig(),
					Protocol.OPC_UA => device.GetConfig<OpcUaConfig>() ?? new OpcUaConfig(),
					_ => new { }
				};
			}
			catch
			{
				config = new { };
			}

			return new DeviceResponseDto
			{
				Id = device.Id,
				Name = device.Name,
				Description = device.Description,
				IsEnabled = device.IsEnabled,
				Protocol = device.Protocol,
				ConnectionConfig = config,
				PollingInterval = device.PollingInterval,
				CreatedAt = device.CreatedAt,
				UpdatedAt = device.UpdatedAt
			};
		}
	}

	public class DeviceFilterDto
	{
		public string? Name { get; set; }
        public string? Description { get; set; }
		public Protocol? Protocol { get; set; }

        public string? Search { get; set; }
		public bool? IsEnabled { get; set; }
		public int Page { get; set; } = 1;
		public int PageSize { get; set; } = 10;
	}
}
