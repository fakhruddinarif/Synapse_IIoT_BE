using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Enums
{
	public enum Protocol
	{
		MODBUS_TCP, // Modbus TCP is a widely used protocol for communication in industrial automation systems. It allows for the exchange of data between devices over a TCP/IP network.
		MODBUS_RTU, // Modbus RTU is a serial communication protocol used in industrial automation systems. It allows for the exchange of data between devices over a serial connection, such as RS-485.
		MQTT, // MQTT (Message Queuing Telemetry Transport) is a lightweight messaging protocol designed for low-bandwidth, high-latency, or unreliable networks. It is commonly used in IoT applications for communication between devices and servers.
		OPC_UA, // OPC UA (Unified Architecture) is a platform-independent, service-oriented architecture for industrial automation. It provides a standardized way for devices and systems to communicate and exchange data.
		HTTP, // HTTP (Hypertext Transfer Protocol) is a widely used protocol for communication on the web. It allows for the exchange of data between clients and servers over a network.
	}
}
