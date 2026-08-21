using Core.Acquisition;
using Core.Enums;
using Core.Interface;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Drivers
{
	/// <summary>
	/// Memilih driver sesuai protokol perangkat.
	///
	/// Protokol yang BELUM punya driver dilempar di sini dengan pesan yang jelas, bukan
	/// dijawab dengan data tiruan. Sebelumnya MQTT, Modbus, dan OPC UA mengembalikan angka
	/// <c>Random</c> — sistem tampak bekerja, grafik bergerak meyakinkan, dan tidak ada satu
	/// pun tanda bahwa yang ditampilkan bukan data pabrik. Kegagalan yang jujur jauh lebih
	/// murah daripada data palsu yang masuk laporan produksi.
	/// </summary>
	public class DeviceDriverFactory : IDeviceDriverFactory
	{
		private static readonly Protocol[] Supported =
			{ Protocol.HTTP, Protocol.MQTT, Protocol.MODBUS_TCP, Protocol.MODBUS_RTU };

		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ILoggerFactory _loggerFactory;

		public DeviceDriverFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
		{
			_httpClientFactory = httpClientFactory;
			_loggerFactory = loggerFactory;
		}

		public IReadOnlyCollection<Protocol> SupportedProtocols => Supported;

		public IDeviceDriver Create(DevicePlan plan) => plan.Protocol switch
		{
			Protocol.HTTP => new HttpDeviceDriver(
				plan, _httpClientFactory, _loggerFactory.CreateLogger<HttpDeviceDriver>()),

			Protocol.MQTT => new MqttDeviceDriver(
				plan, _loggerFactory.CreateLogger<MqttDeviceDriver>()),

			Protocol.MODBUS_TCP or Protocol.MODBUS_RTU => new ModbusDeviceDriver(
				plan, _loggerFactory.CreateLogger<ModbusDeviceDriver>()),

			Protocol.OPC_UA => throw new NotSupportedException(
				"Driver OPC UA belum tersedia (Fase 1). Pustaka OPCFoundation sudah terpasang."),

			_ => throw new NotSupportedException($"Protokol {plan.Protocol} tidak dikenali.")
		};
	}
}
