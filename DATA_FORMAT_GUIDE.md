# 🔍 Synapse IIoT - Data Format Guide untuk Frontend

## 📋 **PENTING: 2 Format Response Berbeda!**

Synapse IIoT menggunakan **2 format response** yang berbeda tergantung dari cara Anda mengakses data:

---

## 1️⃣ **REST API Endpoints** → Selalu Dibungkus `ApiResponse<T>`

### Format Response:

```json
{
  "status": 200,
  "message": "Success message",
  "data": {
    /* Actual data here */
  },
  "paging": {
    /* Optional pagination */
  },
  "error": null
}
```

### Contoh Endpoint:

- ✅ `/api/device` - Get all devices
- ✅ `/api/device/{id}` - Get device by ID
- ✅ `/api/master-table` - Get master tables
- ✅ `/api/storage-flow` - Get storage flows
- ✅ `/api/file/upload` - Upload file

### Contoh Response Nyata:

```json
{
  "status": 200,
  "message": "Devices retrieved successfully",
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "Temperature Sensor",
      "protocol": "HTTP",
      "isEnabled": true
    }
  ],
  "paging": {
    "page": 1,
    "pageSize": 10,
    "totalRecords": 1,
    "totalPages": 1
  },
  "error": null
}
```

---

## 2️⃣ **SignalR Real-Time** → **TIDAK** Dibungkus ApiResponse

### Format Response (DeviceDataDto):

```json
{
  "deviceId": "guid-here",
  "deviceName": "Temperature Sensor",
  "protocol": "HTTP",
  "data": {
    /* Raw device response */
  },
  "timestamp": "2026-02-19T10:30:00Z",
  "status": "success",
  "message": null
}
```

### Event SignalR:

- ✅ `ReceiveDeviceData` - Real-time device data broadcast

### Contoh Response Nyata:

```json
{
  "deviceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "deviceName": "Temperature Sensor",
  "protocol": "HTTP",
  "data": {
    "temperature": 25.5,
    "humidity": 60.2,
    "pressure": 1013.2
  },
  "timestamp": "2026-02-19T10:30:15.123Z",
  "status": "success",
  "message": null
}
```

---

## 🌐 **Testing dengan API Publik (External)**

Synapse IIoT **DAPAT** menarik data dari:

- ✅ Server eksternal (tidak harus localhost)
- ✅ API publik (OpenWeather, JSONPlaceholder, dll)
- ✅ API internal perusahaan lain
- ✅ IoT Cloud Services (AWS IoT, Azure IoT, dll)

### Endpoint Testing External API

**Endpoint**: `POST /api/device/test-external-api`  
**Auth**: Tidak diperlukan (Public endpoint)

---

## 📝 **Contoh Testing: API Publik**

### Contoh 1: JSONPlaceholder (Dummy REST API)

```bash
curl -X POST http://localhost:5000/api/device/test-external-api \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://jsonplaceholder.typicode.com/posts/1",
    "method": "GET"
  }'
```

**Response**:

```json
{
  "status": 200,
  "message": "External API test successful",
  "data": {
    "requestUrl": "https://jsonplaceholder.typicode.com/posts/1",
    "requestMethod": "GET",
    "responseStatusCode": 200,
    "responseData": {
      "userId": 1,
      "id": 1,
      "title": "sunt aut facere repellat provident occaecati excepturi optio reprehenderit",
      "body": "quia et suscipit..."
    },
    "responseHeaders": {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "max-age=43200"
    }
  }
}
```

### Contoh 2: Public Weather API

```bash
curl -X POST http://localhost:5000/api/device/test-external-api \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://api.open-meteo.com/v1/forecast?latitude=52.52&longitude=13.41&current_weather=true",
    "method": "GET"
  }'
```

**Response**:

```json
{
  "status": 200,
  "message": "External API test successful",
  "data": {
    "requestUrl": "https://api.open-meteo.com/v1/forecast?latitude=52.52&longitude=13.41&current_weather=true",
    "requestMethod": "GET",
    "responseStatusCode": 200,
    "responseData": {
      "latitude": 52.52,
      "longitude": 13.419998,
      "current_weather": {
        "temperature": 5.2,
        "windspeed": 12.5,
        "weathercode": 3,
        "time": "2026-02-19T10:00"
      }
    }
  }
}
```

### Contoh 3: Cryptocurrency Price API

```bash
curl -X POST http://localhost:5000/api/device/test-external-api \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://api.coindesk.com/v1/bpi/currentprice.json",
    "method": "GET"
  }'
```

**Response**:

```json
{
  "status": 200,
  "message": "External API test successful",
  "data": {
    "requestUrl": "https://api.coindesk.com/v1/bpi/currentprice.json",
    "requestMethod": "GET",
    "responseStatusCode": 200,
    "responseData": {
      "time": {
        "updated": "Feb 19, 2026 10:30:00 UTC"
      },
      "bpi": {
        "USD": {
          "code": "USD",
          "rate": "52,345.67",
          "rate_float": 52345.67
        }
      }
    }
  }
}
```

---

## 🔧 **Cara Membuat Device dengan External API**

### Step 1: Test API Terlebih Dahulu

```javascript
// Test external API
const testResponse = await fetch(
  "http://localhost:5000/api/device/test-external-api",
  {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      url: "https://api.open-meteo.com/v1/forecast?latitude=-6.2088&longitude=106.8456&current_weather=true",
      method: "GET",
    }),
  },
);

const testResult = await testResponse.json();
console.log("API Test Result:", testResult);

// Check response structure
console.log("Response Data:", testResult.data.responseData);
```

### Step 2: Discover Fields dari Response

```javascript
// Use discover-fields endpoint
const discoverResponse = await fetch(
  "http://localhost:5000/api/storage-flow/discover-fields",
  {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      deviceId: "your-device-id",
    }),
  },
);

const fields = await discoverResponse.json();
console.log("Discovered Fields:", fields.data);

// Output:
// [
//   { path: "$.current_weather.temperature", type: "FLOAT", sampleValue: "5.2" },
//   { path: "$.current_weather.windspeed", type: "FLOAT", sampleValue: "12.5" },
//   { path: "$.current_weather.weathercode", type: "INT32", sampleValue: "3" }
// ]
```

### Step 3: Buat Device dengan External URL

```javascript
const createDevice = await fetch("http://localhost:5000/api/device", {
  method: "POST",
  credentials: "include",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    name: "Jakarta Weather Station",
    description: "Real-time weather data from Open-Meteo API",
    isEnabled: true,
    protocol: "HTTP",
    pollingInterval: 300000, // Poll every 5 minutes
    connectionConfig: {
      url: "https://api.open-meteo.com/v1/forecast?latitude=-6.2088&longitude=106.8456&current_weather=true",
      method: "GET",
    },
  }),
});

const device = await createDevice.json();
console.log("Device Created:", device.data);
```

### Step 4: Buat Storage Flow untuk Simpan Data

```javascript
const createFlow = await fetch("http://localhost:5000/api/storage-flow", {
  method: "POST",
  credentials: "include",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    name: "Weather Data Storage",
    description: "Store Jakarta weather data every 5 minutes",
    isActive: true,
    storageInterval: 300000, // Store every 5 minutes
    masterTableId: "your-master-table-id",
    deviceIds: [device.data.id],
    mappings: [
      {
        masterTableFieldId: "field-temperature-id",
        sourcePath: "$.current_weather.temperature",
      },
      {
        masterTableFieldId: "field-windspeed-id",
        sourcePath: "$.current_weather.windspeed",
      },
      {
        masterTableFieldId: "field-weathercode-id",
        sourcePath: "$.current_weather.weathercode",
      },
    ],
  }),
});

const flow = await createFlow.json();
console.log("Storage Flow Created:", flow.data);
```

### Step 5: Subscribe ke SignalR untuk Real-Time Updates

```javascript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5000/deviceHub", { withCredentials: true })
  .build();

await connection.start();

// Subscribe to device
await connection.invoke("SubscribeToDevice", device.data.id);

// Listen for real-time weather data
connection.on("ReceiveDeviceData", (data) => {
  console.log("Real-time Weather Update:", data);

  // Format response (TIDAK ADA ApiResponse wrapper!)
  // {
  //   deviceId: "guid",
  //   deviceName: "Jakarta Weather Station",
  //   protocol: "HTTP",
  //   data: {
  //     current_weather: {
  //       temperature: 28.5,
  //       windspeed: 15.2,
  //       weathercode: 1
  //     }
  //   },
  //   timestamp: "2026-02-19T10:30:00Z",
  //   status: "success"
  // }

  // Update UI
  document.getElementById("temperature").textContent =
    data.data.current_weather.temperature + "°C";
  document.getElementById("windspeed").textContent =
    data.data.current_weather.windspeed + " km/h";
});
```

---

## 🎯 **Ringkasan untuk Frontend Developer**

### Format Response Berdasarkan Endpoint Type

| **Cara Akses**       | **Format Response**                 | **Dibungkus ApiResponse?** |
| -------------------- | ----------------------------------- | -------------------------- |
| REST API (GET/POST)  | `ApiResponse<T>` dengan data nested | ✅ **YA**                  |
| SignalR Real-Time    | `DeviceDataDto` langsung            | ❌ **TIDAK**               |
| Storage Flow (Query) | Query manual ke database            | N/A (Direct SQL)           |

### Contoh Code untuk Parsing

#### REST API Response:

```javascript
const response = await fetch("/api/device");
const result = await response.json();

// ✅ Akses data lewat result.data
const devices = result.data; // Array of devices
const page = result.paging.page;
const message = result.message;
```

#### SignalR Response:

```javascript
connection.on("ReceiveDeviceData", (deviceData) => {
  // ❌ JANGAN: deviceData.data.data
  // ✅ LANGSUNG: deviceData.data

  const temperature = deviceData.data.temperature;
  const deviceName = deviceData.deviceName;
  const timestamp = deviceData.timestamp;
});
```

---

## 📦 **Contoh Lengkap: React Component**

```jsx
import React, { useState, useEffect } from "react";
import * as signalR from "@microsoft/signalr";

function WeatherDashboard() {
  const [devices, setDevices] = useState([]);
  const [realtimeData, setRealtimeData] = useState(new Map());

  useEffect(() => {
    // 1. Fetch devices via REST API (ApiResponse wrapper)
    fetchDevices();

    // 2. Connect to SignalR for real-time updates (NO ApiResponse wrapper)
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("http://localhost:5000/deviceHub", { withCredentials: true })
      .build();

    connection.start().then(() => {
      // Subscribe to all devices
      devices.forEach((device) => {
        connection.invoke("SubscribeToDevice", device.id);
      });

      // Listen for real-time data
      connection.on("ReceiveDeviceData", (deviceData) => {
        // Update real-time data map
        setRealtimeData((prev) =>
          new Map(prev).set(deviceData.deviceId, deviceData),
        );
      });
    });

    return () => {
      connection.stop();
    };
  }, []);

  async function fetchDevices() {
    const response = await fetch("http://localhost:5000/api/device", {
      credentials: "include",
    });

    const result = await response.json();

    // ✅ REST API: Access via result.data (ApiResponse wrapper)
    if (result.status === 200) {
      setDevices(result.data);
    }
  }

  return (
    <div>
      <h1>Weather Stations Dashboard</h1>

      {devices.map((device) => {
        const liveData = realtimeData.get(device.id);

        return (
          <div key={device.id} className="device-card">
            <h2>{device.name}</h2>
            <p>Protocol: {device.protocol}</p>

            {liveData && (
              <div className="live-data">
                <p>Status: {liveData.status}</p>
                <p>
                  Last Update: {new Date(liveData.timestamp).toLocaleString()}
                </p>

                {/* ✅ SignalR: Access directly via liveData.data (NO ApiResponse wrapper) */}
                {liveData.data.current_weather && (
                  <>
                    <p>
                      Temperature: {liveData.data.current_weather.temperature}°C
                    </p>
                    <p>
                      Wind Speed: {liveData.data.current_weather.windspeed} km/h
                    </p>
                  </>
                )}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

export default WeatherDashboard;
```

---

## ✅ **Kesimpulan**

1. **REST API** → Selalu pakai `result.data` (dibungkus ApiResponse)
2. **SignalR** → Langsung pakai `deviceData.data` (TIDAK dibungkus)
3. **External API** → Bisa digunakan! Test dulu dengan `/test-external-api`
4. **Format konsisten** → Tidak peduli dari mana sumbernya (localhost, external API, cloud)
5. **JSONPath mapping** → Gunakan `$.path.to.value` untuk extract data

---

**Happy Coding! 🚀**
