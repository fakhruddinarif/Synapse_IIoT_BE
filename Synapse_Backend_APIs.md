# Synapse IIoT Core - Complete API Documentation

## 📖 Table of Contents

1. [Overview](#overview)
2. [Authentication](#authentication)
3. [Device Management](#device-management)
4. [Master Table Management](#master-table-management)
5. [Storage Flow](#storage-flow)
6. [File Upload Management](#file-upload-management)
7. [SignalR Real-Time Communication](#signalr-real-time-communication)
8. [Configuration](#configuration)
9. [Frontend Integration Guide](#frontend-integration-guide)
10. [How It Works](#how-it-works)

---

## Overview

Synapse IIoT Core Backend is a comprehensive platform for Industrial IoT data collection, storage, and real-time streaming. It supports multiple protocols (HTTP, MQTT, MODBUS, OPC UA) and provides dynamic table creation with configurable data mappings.

**Base URL**: `http://localhost:5000` (Development)  
**Authentication**: JWT-based with HTTP-only cookies  
**Real-Time**: SignalR WebSocket connection

---

## Authentication

### Overview

Authentication uses **JWT tokens** stored in **HTTP-only cookies** for security. This prevents XSS attacks as JavaScript cannot access the token.

**Key Points**:

- ✅ Token is automatically included in all requests via cookie
- ✅ NO need to manually add Authorization headers
- ✅ Set `credentials: 'include'` in fetch/axios to send cookies
- ✅ Token expires after 60 minutes (configurable)

### 1. Register New User

**Endpoint**: `POST /api/auth/register`

**Request Body**:

```json
{
  "username": "admin",
  "email": "admin@example.com",
  "password": "SecurePass123!",
  "role": "ADMIN"
}
```

**cURL Example**:

```bash
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "username": "admin",
    "email": "admin@example.com",
    "password": "SecurePass123!",
    "role": "ADMIN"
  }'
```

**JavaScript Example (Fetch)**:

```javascript
async function registerUser(username, email, password, role = "USER") {
  try {
    const response = await fetch("http://localhost:5000/api/auth/register", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include", // Important: Include cookies
      body: JSON.stringify({ username, email, password, role }),
    });

    const result = await response.json();

    if (response.ok) {
      console.log("User registered:", result.data);
      return result.data;
    } else {
      console.error("Registration failed:", result.message);
      throw new Error(result.message);
    }
  } catch (error) {
    console.error("Error:", error);
    throw error;
  }
}

// Usage
await registerUser("john_doe", "john@example.com", "MyPassword123!", "USER");
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "User registered successfully",
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "username": "admin",
    "email": "admin@example.com",
    "role": "ADMIN"
  }
}
```

**Validation Rules**:

- Username: 3-50 characters
- Email: Valid email format
- Password: Minimum 6 characters
- Role: ADMIN, USER, or VIEWER

### 2. Login

**Endpoint**: `POST /api/auth/login`

**Request Body**:

```json
{
  "email": "admin@example.com",
  "password": "SecurePass123!"
}
```

**cURL Example**:

```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -c cookies.txt \
  -d '{
    "email": "admin@example.com",
    "password": "SecurePass123!"
  }'
```

**Note**: `-c cookies.txt` saves the cookie to a file for subsequent requests.

**JavaScript Example**:

```javascript
async function login(email, password) {
  try {
    const response = await fetch("http://localhost:5000/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include", // Critical: Allows cookie to be set
      body: JSON.stringify({ email, password }),
    });

    const result = await response.json();

    if (response.ok) {
      console.log("Login successful:", result.data);

      // Store user info in localStorage (optional, for UI display)
      localStorage.setItem("user", JSON.stringify(result.data));

      // Redirect to dashboard
      window.location.href = "/dashboard";

      return result.data;
    } else {
      throw new Error(result.message || "Invalid credentials");
    }
  } catch (error) {
    console.error("Login error:", error);
    alert(`Login failed: ${error.message}`);
    throw error;
  }
}

// Usage
document.getElementById("loginForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  const email = document.getElementById("email").value;
  const password = document.getElementById("password").value;
  await login(email, password);
});
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Login successful",
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "username": "admin",
    "email": "admin@example.com",
    "role": "ADMIN",
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
  }
}
```

**Important**: The `token` field is for reference only. The actual token is set in an HTTP-only cookie named `auth_token` that is automatically sent with subsequent requests.

**Response Headers**:

```
Set-Cookie: auth_token=eyJhbGc...; Path=/; HttpOnly; SameSite=Lax; Max-Age=3600
```

### 3. Get Current User Info

**Endpoint**: `GET /api/auth/me`  
**Auth**: Required (Cookie)

**cURL Example**:

```bash
curl -X GET http://localhost:5000/api/auth/me \
  -b cookies.txt
```

**JavaScript Example**:

```javascript
async function getCurrentUser() {
  try {
    const response = await fetch("http://localhost:5000/api/auth/me", {
      method: "GET",
      credentials: "include", // Sends auth cookie
    });

    if (!response.ok) {
      if (response.status === 401) {
        // Not authenticated, redirect to login
        window.location.href = "/login";
        return null;
      }
      throw new Error("Failed to get user info");
    }

    const result = await response.json();
    return result.data;
  } catch (error) {
    console.error("Error fetching user:", error);
    return null;
  }
}

// Usage: Check authentication on page load
window.addEventListener("DOMContentLoaded", async () => {
  const user = await getCurrentUser();

  if (user) {
    console.log("Logged in as:", user.username);
    document.getElementById("userName").textContent = user.username;
    document.getElementById("userRole").textContent = user.role;
  } else {
    console.log("Not authenticated");
  }
});
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "User info retrieved",
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "username": "admin",
    "email": "admin@example.com",
    "role": "ADMIN"
  }
}
```

**Response** (401 Unauthorized):

```json
{
  "status": 401,
  "message": "Unauthorized"
}
```

### 4. Logout

**Endpoint**: `POST /api/auth/logout`  
**Auth**: Required (Cookie)

**cURL Example**:

```bash
curl -X POST http://localhost:5000/api/auth/logout \
  -b cookies.txt \
  -c cookies.txt
```

**JavaScript Example**:

```javascript
async function logout() {
  try {
    const response = await fetch("http://localhost:5000/api/auth/logout", {
      method: "POST",
      credentials: "include",
    });

    const result = await response.json();

    if (response.ok) {
      console.log("Logged out successfully");

      // Clear local storage
      localStorage.removeItem("user");

      // Redirect to login page
      window.location.href = "/login";
    }
  } catch (error) {
    console.error("Logout error:", error);
  }
}

// Usage
document.getElementById("logoutButton").addEventListener("click", logout);
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Logged out successfully"
}
```

**Important**: The server clears the `auth_token` cookie by setting its expiration to the past.

### Authentication Flow Diagram

```
┌─────────────┐                           ┌─────────────┐
│   Browser   │                           │   Server    │
└──────┬──────┘                           └──────┬──────┘
       │                                          │
       │  POST /api/auth/login                    │
       │  { email, password }                     │
       ├─────────────────────────────────────────>│
       │                                          │
       │                  Validate credentials    │
       │                  Generate JWT token      │
       │                                          │
       │  200 OK                                  │
       │  Set-Cookie: auth_token=...              │
       │<─────────────────────────────────────────┤
       │                                          │
   [Cookie stored                                 │
    in browser]                                   │
       │                                          │
       │  GET /api/device                         │
       │  Cookie: auth_token=...                  │
       ├─────────────────────────────────────────>│
       │                                          │
       │                      Validate JWT token  │
       │                      from cookie         │
       │                                          │
       │  200 OK                                  │
       │  { devices data }                        │
       │<─────────────────────────────────────────┤
       │                                          │
```

### Role-Based Access Control

Different endpoints require different roles:

| Endpoint Category | Required Role | Notes                        |
| ----------------- | ------------- | ---------------------------- |
| Authentication    | None (Public) | Register, Login              |
| Device Read       | VIEWER+       | Get devices, Get by ID       |
| Device Write      | USER+         | Create, Update, Delete       |
| Master Table      | USER+         | All operations               |
| Storage Flow      | USER+         | All operations               |
| File Upload       | USER+         | Upload, Delete               |
| File Config       | None (Public) | Get allowed types and limits |

**Role Hierarchy**:

- VIEWER: Read-only access
- USER: Read + Write access
- ADMIN: Full access (including user management)

---

## Device Management

### 1. Get All Devices

**Endpoint**: `GET /api/device?page=1&pageSize=10`  
**Auth**: Required

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Devices retrieved successfully",
  "data": [
    {
      "id": "device-guid-1",
      "name": "Sensor-001",
      "description": "Temperature Sensor",
      "isEnabled": true,
      "protocol": "HTTP",
      "pollingInterval": 5000,
      "connectionConfig": {
        "url": "http://192.168.1.100/api/sensor",
        "method": "GET"
      },
      "createdAt": "2026-02-18T10:30:00Z",
      "updatedAt": "2026-02-18T10:30:00Z"
    }
  ],
  "paging": {
    "page": 1,
    "pageSize": 10,
    "totalRecords": 1,
    "totalPages": 1
  }
}
```

### 2. Get Device by ID

**Endpoint**: `GET /api/device/{id}`  
**Auth**: Required

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Device retrieved successfully",
  "data": {
    "id": "device-guid-1",
    "name": "Sensor-001",
    "description": "Temperature Sensor",
    "isEnabled": true,
    "protocol": "HTTP",
    "pollingInterval": 5000,
    "connectionConfig": {
      "url": "http://192.168.1.100/api/sensor",
      "method": "GET",
      "headers": {
        "Authorization": "Bearer token123"
      }
    },
    "createdAt": "2026-02-18T10:30:00Z",
    "updatedAt": "2026-02-18T10:30:00Z"
  }
}
```

### 3. Create Device

**Endpoint**: `POST /api/device`  
**Auth**: Required

**Request Body** (HTTP Device):

```json
{
  "name": "Sensor-001",
  "description": "Temperature & Humidity Sensor",
  "isEnabled": true,
  "protocol": "HTTP",
  "pollingInterval": 5000,
  "connectionConfig": {
    "url": "http://192.168.1.100/api/sensor",
    "method": "GET",
    "headers": {
      "Authorization": "Bearer your-token"
    }
  }
}
```

**Request Body** (MODBUS Device):

```json
{
  "name": "Modbus-RTU-001",
  "description": "RTU Device",
  "isEnabled": true,
  "protocol": "MODBUS_RTU",
  "pollingInterval": 10000,
  "connectionConfig": {
    "port": "COM3",
    "baudRate": 9600,
    "slaveId": 1
  }
}
```

**Response** (201 Created):

```json
{
  "status": 201,
  "message": "Device created successfully",
  "data": {
    "id": "new-device-guid",
    "name": "Sensor-001",
    ...
  }
}
```

### 4. Update Device

**Endpoint**: `PUT /api/device/{id}`  
**Auth**: Required

**Request Body**:

```json
{
  "name": "Sensor-001-Updated",
  "description": "Updated description",
  "isEnabled": false,
  "protocol": "HTTP",
  "pollingInterval": 10000,
  "connectionConfig": {
    "url": "http://192.168.1.100/api/sensor/v2",
    "method": "POST"
  }
}
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Device updated successfully",
  "data": { ... }
}
```

### 5. Delete Device

**Endpoint**: `DELETE /api/device/{id}`  
**Auth**: Required

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Device deleted successfully"
}
```

**Note**: This is a soft delete. Device is marked as deleted but not physically removed.

### 6. Test HTTP Device Connection

**Endpoint**: `POST /api/device/http-test`  
**Auth**: Required

**Request Body**:

```json
{
  "url": "http://192.168.1.100/api/sensor",
  "method": "GET",
  "headers": {
    "Authorization": "Bearer test-token"
  },
  "body": null
}
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "HTTP test successful",
  "data": {
    "message": "Test",
    "status": 200,
    "data": {
      "table": "test_table",
      "desc": "desc test"
    }
  }
}
```

---

## Master Table Management

### 1. Get All Master Tables

**Endpoint**: `GET /api/master-table?page=1&pageSize=10`  
**Auth**: Required

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Master tables retrieved successfully",
  "data": [
    {
      "id": "table-guid-1",
      "name": "SensorData",
      "tableName": "sensor_data_table",
      "description": "Sensor readings storage",
      "isActive": true,
      "fields": [
        {
          "id": "field-guid-1",
          "name": "temperature",
          "dataType": "FLOAT",
          "isEnabled": true
        }
      ],
      "createdAt": "2026-02-18T10:00:00Z"
    }
  ],
  "paging": { ... }
}
```

### 2. Get Master Table by ID

**Endpoint**: `GET /api/master-table/{id}`  
**Auth**: Required

### 3. Create Master Table

**Endpoint**: `POST /api/master-table`  
**Auth**: Required

**Request Body**:

```json
{
  "name": "SensorData",
  "tableName": "sensor_data_table",
  "description": "Storage for sensor readings",
  "isActive": true,
  "fields": [
    {
      "name": "temperature",
      "dataType": "FLOAT",
      "isEnabled": true
    },
    {
      "name": "humidity",
      "dataType": "FLOAT",
      "isEnabled": true
    },
    {
      "name": "status",
      "dataType": "STRING",
      "isEnabled": true
    },
    {
      "name": "timestamp_custom",
      "dataType": "DATETIME",
      "isEnabled": true
    }
  ]
}
```

**Response** (201 Created):

```json
{
  "status": 201,
  "message": "Master table created successfully",
  "data": { ... }
}
```

**Note**: When `isActive: true`, a physical database table is automatically created with schema matching the fields.

### 4. Update Master Table

**Endpoint**: `PUT /api/master-table/{id}`  
**Auth**: Required

### 5. Delete Master Table

**Endpoint**: `DELETE /api/master-table/{id}`  
**Auth**: Required

### 6. Add Field to Master Table

**Endpoint**: `POST /api/master-table/{masterTableId}/field`  
**Auth**: Required

**Request Body**:

```json
{
  "name": "pressure",
  "dataType": "FLOAT",
  "isEnabled": true
}
```

### 7. Update Field

**Endpoint**: `PUT /api/master-table/{masterTableId}/field/{fieldId}`  
**Auth**: Required

### 8. Delete Field

**Endpoint**: `DELETE /api/master-table/{masterTableId}/field/{fieldId}`  
**Auth**: Required

### 9. Get All Fields of a Master Table

**Endpoint**: `GET /api/master-table/{masterTableId}/fields`  
**Auth**: Required

---

## Storage Flow

Storage Flow enables automatic data extraction from devices and storage into master tables.

### 1. Get All Storage Flows

**Endpoint**: `GET /api/storage-flow?page=1&pageSize=10`  
**Auth**: Required

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Storage flows retrieved successfully",
  "data": [
    {
      "id": "flow-guid-1",
      "name": "Sensor Data Flow",
      "description": "Store sensor data every 10 seconds",
      "isActive": true,
      "storageInterval": 10000,
      "masterTableId": "table-guid-1",
      "masterTableName": "SensorData",
      "devices": [
        {
          "deviceId": "device-guid-1",
          "deviceName": "Sensor-001"
        }
      ],
      "mappings": [
        {
          "id": "mapping-guid-1",
          "masterTableFieldId": "field-guid-1",
          "masterTableFieldName": "temperature",
          "sourcePath": "$.data.temperature",
          "tagId": null
        }
      ],
      "createdAt": "2026-02-18T12:00:00Z"
    }
  ],
  "paging": { ... }
}
```

### 2. Get Storage Flow by ID

**Endpoint**: `GET /api/storage-flow/{id}`  
**Auth**: Required

### 3. Create Storage Flow

**Endpoint**: `POST /api/storage-flow`  
**Auth**: Required

**Request Body** (HTTP/MQTT with JSONPath):

```json
{
  "name": "Sensor Data Flow",
  "description": "Automatic data storage from sensors",
  "isActive": true,
  "storageInterval": 10000,
  "masterTableId": "table-guid-1",
  "deviceIds": ["device-guid-1", "device-guid-2"],
  "mappings": [
    {
      "masterTableFieldId": "field-temperature-guid",
      "sourcePath": "$.data.temperature"
    },
    {
      "masterTableFieldId": "field-humidity-guid",
      "sourcePath": "$.data.humidity"
    },
    {
      "masterTableFieldId": "field-status-guid",
      "sourcePath": "$.message"
    }
  ]
}
```

**Request Body** (MODBUS/OPC UA with Tags):

```json
{
  "name": "MODBUS Data Flow",
  "description": "Store MODBUS register values",
  "isActive": true,
  "storageInterval": 15000,
  "masterTableId": "table-guid-1",
  "deviceIds": ["modbus-device-guid"],
  "mappings": [
    {
      "masterTableFieldId": "field-temperature-guid",
      "sourcePath": "Temp_Sensor_1",
      "tagId": "tag-guid-1"
    },
    {
      "masterTableFieldId": "field-pressure-guid",
      "sourcePath": "Pressure_Sensor_1",
      "tagId": "tag-guid-2"
    }
  ]
}
```

**Response** (201 Created):

```json
{
  "status": 201,
  "message": "Storage flow created successfully",
  "data": { ... }
}
```

### 4. Update Storage Flow

**Endpoint**: `PUT /api/storage-flow/{id}`  
**Auth**: Required

### 5. Delete Storage Flow

**Endpoint**: `DELETE /api/storage-flow/{id}`  
**Auth**: Required

**Note**: Soft delete. The background worker will automatically stop processing this flow.

### 6. Discover Fields from Device

This powerful endpoint helps you automatically discover available fields from a device response.

**Endpoint**: `POST /api/storage-flow/discover-fields`  
**Auth**: Required

**Request Body**:

```json
{
  "deviceId": "device-guid-1"
}
```

**How it works**:

#### For HTTP/MQTT Devices:

1. Service fetches data from the device
2. Parses JSON response
3. Auto-generates JSONPath expressions for all fields
4. Returns suggested field mappings

**Example Device Response**:

```json
{
  "message": "Success",
  "code": 200,
  "data": {
    "table": "test_table",
    "desc": "Description here",
    "sensors": {
      "temperature": 25.5,
      "humidity": 60.2
    },
    "values": [100, 200, 300]
  }
}
```

**API Response** (200 OK):

```json
{
  "status": 200,
  "message": "Fields discovered successfully",
  "data": [
    {
      "path": "$.message",
      "type": "STRING",
      "sampleValue": "Success"
    },
    {
      "path": "$.code",
      "type": "INT32",
      "sampleValue": "200"
    },
    {
      "path": "$.data.table",
      "type": "STRING",
      "sampleValue": "test_table"
    },
    {
      "path": "$.data.desc",
      "type": "STRING",
      "sampleValue": "Description here"
    },
    {
      "path": "$.data.sensors.temperature",
      "type": "FLOAT",
      "sampleValue": "25.5"
    },
    {
      "path": "$.data.sensors.humidity",
      "type": "FLOAT",
      "sampleValue": "60.2"
    },
    {
      "path": "$.data.values[0]",
      "type": "INT32",
      "sampleValue": "100"
    },
    {
      "path": "$.data.values[1]",
      "type": "INT32",
      "sampleValue": "200"
    },
    {
      "path": "$.data.values[2]",
      "type": "INT32",
      "sampleValue": "300"
    }
  ]
}
```

You can then use these `path` values as `sourcePath` when creating storage flow mappings.

#### For MODBUS/OPC UA Devices:

1. Service queries all Tags associated with the device
2. Returns list of Tag names and addresses
3. You use Tag name or TagId for mapping

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Tags discovered successfully",
  "data": [
    {
      "path": "Temp_Sensor_1",
      "type": "FLOAT",
      "sampleValue": "Tag-based",
      "tagId": "tag-guid-1"
    },
    {
      "path": "Pressure_Sensor_1",
      "type": "INT16",
      "sampleValue": "Tag-based",
      "tagId": "tag-guid-2"
    }
  ]
}
```

---

## File Upload Management

### Overview

The File Upload API provides secure file upload capabilities with configurable file type restrictions, size limits, and entity-scoped storage.

**Security Features**:

- ✅ File extension whitelist validation
- ✅ File size limits (configurable per environment)
- ✅ Double-extension attack prevention
- ✅ GUID-based file naming (prevents path traversal)
- ✅ Entity-scoped uploads (track which file belongs to which entity)
- ✅ Soft delete support with database metadata tracking

**Allowed File Types** (Configurable):

- Images: `.jpg`, `.jpeg`, `.png`, `.gif`, `.svg`, `.webp`
- Documents: `.pdf`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.txt`, `.csv`
- Archives: `.zip`, `.rar`

**File Size Limits**:

- Production: 10 MB
- Development: 50 MB

### 1. Upload Single File

**Endpoint**: `POST /api/file/upload`  
**Auth**: Required  
**Content-Type**: `multipart/form-data`

**Request Parameters**:

- `file` (IFormFile, required): The file to upload
- `subDirectory` (query string, optional): Subdirectory for organization (e.g., "documents", "images")

**Frontend Example (JavaScript - Fetch API)**:

```javascript
async function uploadSingleFile(file, subDirectory = null) {
  const formData = new FormData();
  formData.append("file", file);

  let url = "http://localhost:5000/api/file/upload";
  if (subDirectory) {
    url += `?subDirectory=${encodeURIComponent(subDirectory)}`;
  }

  try {
    const response = await fetch(url, {
      method: "POST",
      credentials: "include", // Include auth cookie
      body: formData,
      // Don't set Content-Type header - browser will set it with boundary
    });

    const result = await response.json();

    if (response.ok) {
      console.log("File uploaded:", result.data);
      // result.data contains: { filePath, fileName, fileSize, contentType, uploadedAt }
      return result.data;
    } else {
      console.error("Upload failed:", result.message);
      throw new Error(result.message);
    }
  } catch (error) {
    console.error("Upload error:", error);
    throw error;
  }
}

// Usage in a form component
document.getElementById("fileInput").addEventListener("change", async (e) => {
  const file = e.target.files[0];
  if (file) {
    try {
      const result = await uploadSingleFile(file, "documents");
      alert(`File uploaded successfully: ${result.fileName}`);
    } catch (error) {
      alert(`Upload failed: ${error.message}`);
    }
  }
});
```

**Frontend Example (JavaScript - Axios)**:

```javascript
import axios from "axios";

async function uploadSingleFileAxios(file, subDirectory = null) {
  const formData = new FormData();
  formData.append("file", file);

  const params = subDirectory ? { subDirectory } : {};

  try {
    const response = await axios.post(
      "http://localhost:5000/api/file/upload",
      formData,
      {
        params: params,
        withCredentials: true,
        headers: {
          "Content-Type": "multipart/form-data",
        },
        onUploadProgress: (progressEvent) => {
          const percentCompleted = Math.round(
            (progressEvent.loaded * 100) / progressEvent.total,
          );
          console.log(`Upload Progress: ${percentCompleted}%`);
          // Update progress bar UI here
        },
      },
    );

    return response.data.data;
  } catch (error) {
    if (error.response) {
      throw new Error(error.response.data.message);
    }
    throw error;
  }
}
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "File uploaded successfully",
  "data": {
    "filePath": "uploads/documents/3fa85f64-5717-4562-b3fc-2c963f66afa6.pdf",
    "fileName": "3fa85f64-5717-4562-b3fc-2c963f66afa6.pdf",
    "fileSize": 1048576,
    "contentType": "application/pdf",
    "uploadedAt": "2026-02-19T10:30:00Z"
  }
}
```

**Error Response** (400 Bad Request):

```json
{
  "status": 400,
  "message": "File type .exe is not allowed. Allowed types: .jpg, .jpeg, .png, .pdf, ..."
}
```

### 2. Upload Multiple Files

**Endpoint**: `POST /api/file/upload-multiple`  
**Auth**: Required  
**Content-Type**: `multipart/form-data`

**Request Parameters**:

- `files` (List<IFormFile>, required): Array of files to upload
- `subDirectory` (query string, optional): Subdirectory for organization

**Frontend Example (React Component)**:

```jsx
import React, { useState } from "react";
import axios from "axios";

function MultipleFileUpload() {
  const [selectedFiles, setSelectedFiles] = useState([]);
  const [uploading, setUploading] = useState(false);
  const [uploadResults, setUploadResults] = useState(null);

  const handleFileChange = (e) => {
    setSelectedFiles(Array.from(e.target.files));
  };

  const handleUpload = async () => {
    if (selectedFiles.length === 0) {
      alert("Please select files first");
      return;
    }

    const formData = new FormData();
    selectedFiles.forEach((file) => {
      formData.append("files", file);
    });

    setUploading(true);

    try {
      const response = await axios.post(
        "http://localhost:5000/api/file/upload-multiple",
        formData,
        {
          params: { subDirectory: "images" },
          withCredentials: true,
          headers: { "Content-Type": "multipart/form-data" },
        },
      );

      setUploadResults(response.data.data);
      alert(
        `${response.data.data.successCount} files uploaded, ${response.data.data.failedCount} failed`,
      );
    } catch (error) {
      alert(`Upload error: ${error.response?.data?.message || error.message}`);
    } finally {
      setUploading(false);
    }
  };

  return (
    <div>
      <input
        type="file"
        multiple
        onChange={handleFileChange}
        accept=".jpg,.jpeg,.png,.pdf"
      />
      <button onClick={handleUpload} disabled={uploading}>
        {uploading ? "Uploading..." : "Upload Files"}
      </button>

      {uploadResults && (
        <div>
          <p>Success: {uploadResults.successCount}</p>
          <p>Failed: {uploadResults.failedCount}</p>

          {uploadResults.uploadedFiles.map((file, index) => (
            <div key={index}>✅ {file.fileName}</div>
          ))}

          {uploadResults.failedFiles.map((error, index) => (
            <div key={index} style={{ color: "red" }}>
              ❌ {error}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default MultipleFileUpload;
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "3 file(s) uploaded successfully, 1 failed",
  "data": {
    "uploadedFiles": [
      {
        "filePath": "uploads/images/guid-1.jpg",
        "fileName": "guid-1.jpg",
        "fileSize": 524288,
        "contentType": "image/jpeg",
        "uploadedAt": "2026-02-19T10:30:00Z"
      },
      {
        "filePath": "uploads/images/guid-2.png",
        "fileName": "guid-2.png",
        "fileSize": 724288,
        "contentType": "image/png",
        "uploadedAt": "2026-02-19T10:30:01Z"
      },
      {
        "filePath": "uploads/images/guid-3.pdf",
        "fileName": "guid-3.pdf",
        "fileSize": 1048576,
        "contentType": "application/pdf",
        "uploadedAt": "2026-02-19T10:30:02Z"
      }
    ],
    "failedFiles": [
      "large-file.jpg: File size exceeds maximum allowed size of 10485760 bytes"
    ],
    "successCount": 3,
    "failedCount": 1
  }
}
```

### 3. Upload File for Entity Field

Upload a file associated with a specific entity (e.g., device logo, user avatar, master table attachment).

**Endpoint**: `POST /api/file/upload-field`  
**Auth**: Required  
**Content-Type**: `multipart/form-data`

**Request Parameters**:

- `file` (IFormFile, required): The file to upload
- `entityType` (query string, required): Entity type (e.g., "Device", "User", "MasterTable")
- `entityId` (query string, required): Entity GUID
- `fieldName` (query string, required): Field name (e.g., "logo", "avatar", "attachment")

**Frontend Example (Upload Device Logo)**:

```javascript
async function uploadDeviceLogo(deviceId, logoFile) {
  const formData = new FormData();
  formData.append("file", logoFile);

  const params = new URLSearchParams({
    entityType: "Device",
    entityId: deviceId,
    fieldName: "logo",
  });

  try {
    const response = await fetch(
      `http://localhost:5000/api/file/upload-field?${params}`,
      {
        method: "POST",
        credentials: "include",
        body: formData,
      },
    );

    const result = await response.json();

    if (response.ok) {
      console.log("Logo uploaded:", result.data.filePath);
      // Update device logo preview in UI
      document.getElementById("deviceLogo").src =
        `http://localhost:5000/${result.data.filePath}`;
      return result.data;
    } else {
      throw new Error(result.message);
    }
  } catch (error) {
    console.error("Logo upload failed:", error);
    throw error;
  }
}

// Usage in device edit form
document.getElementById("logoInput").addEventListener("change", async (e) => {
  const file = e.target.files[0];
  const deviceId = document.getElementById("deviceId").value;

  if (file && deviceId) {
    // Client-side validation before upload
    const allowedTypes = ["image/jpeg", "image/png", "image/svg+xml"];
    if (!allowedTypes.includes(file.type)) {
      alert("Only JPG, PNG, and SVG logos are allowed");
      return;
    }

    const maxSize = 2 * 1024 * 1024; // 2MB
    if (file.size > maxSize) {
      alert("Logo must be smaller than 2MB");
      return;
    }

    try {
      await uploadDeviceLogo(deviceId, file);
      alert("Logo uploaded successfully!");
    } catch (error) {
      alert(`Upload failed: ${error.message}`);
    }
  }
});
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "File uploaded for Device.logo",
  "data": {
    "filePath": "uploads/Device/3fa85f64-5717-4562-b3fc-2c963f66afa6/logo/guid-filename.png",
    "fileName": "guid-filename.png",
    "fileSize": 524288,
    "contentType": "image/png",
    "uploadedAt": "2026-02-19T10:30:00Z"
  }
}
```

**Note**: If a file already exists for this entity/field combination, it will be **automatically replaced** (old file deleted, new file uploaded).

### 4. Delete File

**Endpoint**: `DELETE /api/file/delete`  
**Auth**: Required

**Request Parameters**:

- `filePath` (query string, required): Relative file path from uploads root

**Frontend Example**:

```javascript
async function deleteFile(filePath) {
  const params = new URLSearchParams({ filePath });

  try {
    const response = await fetch(
      `http://localhost:5000/api/file/delete?${params}`,
      {
        method: "DELETE",
        credentials: "include",
      },
    );

    const result = await response.json();

    if (response.ok) {
      console.log("File deleted successfully");
      return true;
    } else if (response.status === 404) {
      console.warn("File not found");
      return false;
    } else {
      throw new Error(result.message);
    }
  } catch (error) {
    console.error("Delete failed:", error);
    throw error;
  }
}

// Usage
const filePathToDelete = "uploads/documents/guid-file.pdf";
await deleteFile(filePathToDelete);
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "File deleted successfully"
}
```

**Response** (404 Not Found):

```json
{
  "status": 404,
  "message": "File not found"
}
```

### 5. Get Upload Configuration

Get allowed file extensions and size limits. Useful for client-side validation.

**Endpoint**: `GET /api/file/config`  
**Auth**: Not Required (Public endpoint)

**Frontend Example**:

```javascript
async function getUploadConfig() {
  try {
    const response = await fetch("http://localhost:5000/api/file/config");
    const result = await response.json();

    if (response.ok) {
      return result.data;
      // {
      //   allowedExtensions: [".jpg", ".jpeg", ".png", ...],
      //   maxFileSize: 10485760,
      //   maxFileSizeMB: 10.0
      // }
    }
  } catch (error) {
    console.error("Failed to fetch upload config:", error);
    return null;
  }
}

// Usage: Build dynamic file input accept attribute
async function initializeFileUpload() {
  const config = await getUploadConfig();

  if (config) {
    // Set file input accept attribute
    const fileInput = document.getElementById("fileUpload");
    fileInput.setAttribute("accept", config.allowedExtensions.join(","));

    // Display max file size to user
    document.getElementById("maxSizeInfo").textContent =
      `Maximum file size: ${config.maxFileSizeMB} MB`;

    // Use for client-side validation
    fileInput.addEventListener("change", (e) => {
      const file = e.target.files[0];
      if (file) {
        const ext = "." + file.name.split(".").pop().toLowerCase();

        if (!config.allowedExtensions.includes(ext)) {
          alert(`File type ${ext} is not allowed`);
          e.target.value = "";
          return;
        }

        if (file.size > config.maxFileSize) {
          alert(`File size must be less than ${config.maxFileSizeMB}MB`);
          e.target.value = "";
          return;
        }
      }
    });
  }
}

initializeFileUpload();
```

**Response** (200 OK):

```json
{
  "status": 200,
  "message": "Upload configuration retrieved",
  "data": {
    "allowedExtensions": [
      ".jpg",
      ".jpeg",
      ".png",
      ".gif",
      ".svg",
      ".webp",
      ".pdf",
      ".doc",
      ".docx",
      ".xls",
      ".xlsx",
      ".txt",
      ".csv",
      ".zip",
      ".rar"
    ],
    "maxFileSize": 10485760,
    "maxFileSizeMB": 10.0
  }
}
```

### Client-Side Validation Helper

Complete validation function for frontend:

```javascript
class FileUploadValidator {
  constructor() {
    this.config = null;
  }

  async initialize() {
    const response = await fetch("http://localhost:5000/api/file/config");
    const result = await response.json();
    this.config = result.data;
  }

  validateFile(file) {
    const errors = [];

    if (!this.config) {
      errors.push("Upload configuration not loaded");
      return { valid: false, errors };
    }

    // Check file extension
    const ext = "." + file.name.split(".").pop().toLowerCase();
    if (!this.config.allowedExtensions.includes(ext)) {
      errors.push(
        `File type ${ext} is not allowed. Allowed: ${this.config.allowedExtensions.join(", ")}`,
      );
    }

    // Check file size
    if (file.size > this.config.maxFileSize) {
      errors.push(
        `File size ${this.formatBytes(file.size)} exceeds maximum ${this.config.maxFileSizeMB}MB`,
      );
    }

    // Check for double extensions (security)
    const parts = file.name.split(".");
    if (parts.length > 2) {
      errors.push("Files with multiple extensions are not allowed");
    }

    return {
      valid: errors.length === 0,
      errors,
    };
  }

  formatBytes(bytes) {
    if (bytes === 0) return "0 Bytes";
    const k = 1024;
    const sizes = ["Bytes", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + " " + sizes[i];
  }
}

// Usage
const validator = new FileUploadValidator();
await validator.initialize();

document.getElementById("fileInput").addEventListener("change", (e) => {
  const file = e.target.files[0];
  if (file) {
    const validation = validator.validateFile(file);

    if (!validation.valid) {
      alert(validation.errors.join("\n"));
      e.target.value = "";
    }
  }
});
```

---

## SignalR Real-Time Communication

### Connection

**Endpoint**: `/deviceHub` (WebSocket)

**Client Connection** (JavaScript):

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5000/deviceHub", {
    withCredentials: true, // Include auth cookie
  })
  .configureLogging(signalR.LogLevel.Information)
  .build();

await connection.start();
console.log("Connected to DeviceDataHub");
```

### Real-Time Broadcast Behavior

**When does SignalR broadcast data?**

SignalR broadcasts device data in **real-time** based on the following trigger:

1. **Device Polling**: The `DeviceWorkerService` continuously polls enabled devices based on their `pollingInterval`
2. **Automatic Broadcast**: Every time fresh data is received from a device, it is immediately broadcast to:
   - `Clients.All` - All connected clients (global broadcast)
   - `Clients.Group(deviceId)` - Clients subscribed to that specific device

**You don't need to manually request data**. Once a client connects and subscribes to a device, data flows automatically as the backend polls the device.

### SignalR Hub Methods

#### 1. OnConnectedAsync (Automatic)

When client connects, receives confirmation:

```javascript
connection.on("Connected", (data) => {
  console.log("Connection ID:", data.connectionId);
  console.log("Message:", data.message);
  console.log("Timestamp:", data.timestamp);
});
```

#### 2. Subscribe to Specific Device

Subscribe to receive updates only from a specific device:

```javascript
await connection.invoke("SubscribeToDevice", "device-guid-1");

connection.on("Subscribed", (data) => {
  console.log(`Subscribed to device: ${data.deviceId}`);
  console.log(`Group name: ${data.groupName}`); // e.g., "synapse_device_device-guid-1"
});
```

#### 3. Receive Device Data

```javascript
connection.on("ReceiveDeviceData", (data) => {
  console.log("Device ID:", data.deviceId);
  console.log("Device Name:", data.deviceName);
  console.log("Protocol:", data.protocol);
  console.log("Status:", data.status); // "success" or "error"
  console.log("Data:", data.data); // Actual device response
  console.log("Timestamp:", data.timestamp);

  // Example data structure:
  // {
  //   deviceId: "device-guid-1",
  //   deviceName: "Sensor-001",
  //   protocol: "HTTP",
  //   status: "success",
  //   data: {
  //     message: "Test",
  //     data: {
  //       temperature: 25.5,
  //       humidity: 60.2
  //     }
  //   },
  //   timestamp: "2026-02-18T14:30:00Z"
  // }
});
```

#### 4. Unsubscribe from Device

```javascript
await connection.invoke("UnsubscribeFromDevice", "device-guid-1");

connection.on("Unsubscribed", (data) => {
  console.log(`Unsubscribed from device: ${data.deviceId}`);
});
```

#### 5. Get Connection Info

```javascript
await connection.invoke("GetConnectionInfo");

connection.on("ConnectionInfo", (data) => {
  console.log("Connection ID:", data.connectionId);
  console.log("Timestamp:", data.timestamp);
});
```

### SignalR Group Naming Security

Group names use **configurable prefixes** defined in `appsettings.json` to prevent hardcoded values and support multi-environment deployments:

**Production** (`appsettings.json`):

```json
{
  "SignalRSettings": {
    "GroupPrefix": {
      "Device": "synapse_device_",
      "StorageFlow": "synapse_flow_",
      "MasterTable": "synapse_table_"
    },
    "EnableDetailedErrors": false,
    "ClientTimeoutInterval": "00:01:00",
    "KeepAliveInterval": "00:00:30"
  }
}
```

**Development** (`appsettings.Development.json`):

```json
{
  "SignalRSettings": {
    "GroupPrefix": {
      "Device": "synapse_dev_device_",
      "StorageFlow": "synapse_dev_flow_",
      "MasterTable": "synapse_dev_table_"
    },
    "EnableDetailedErrors": true,
    "ClientTimeoutInterval": "00:01:00",
    "KeepAliveInterval": "00:00:30"
  }
}
```

**Example Group Names**:

- Production: `synapse_device_3fa85f64-5717-4562-b3fc-2c963f66afa6`
- Development: `synapse_dev_device_3fa85f64-5717-4562-b3fc-2c963f66afa6`

This allows:

- Environment isolation (dev/staging/prod cannot interfere)
- Easier debugging with verbose group names
- Centralized configuration management
- No hardcoded strings in code

### Complete SignalR Client Example

```javascript
// Initialize connection
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5000/deviceHub", {
    withCredentials: true,
  })
  .withAutomaticReconnect()
  .configureLogging(signalR.LogLevel.Information)
  .build();

// Set up event listeners
connection.on("Connected", (data) => {
  console.log("✅ Connected:", data);
});

connection.on("Subscribed", (data) => {
  console.log("📡 Subscribed:", data);
});

connection.on("ReceiveDeviceData", (data) => {
  console.log("📊 Device Data:", data);

  // Update UI with real-time data
  if (data.status === "success") {
    updateDashboard(data.deviceId, data.data);
  } else {
    showError(data.deviceId, data.message);
  }
});

connection.on("Unsubscribed", (data) => {
  console.log("🔕 Unsubscribed:", data);
});

// Handle reconnection
connection.onreconnecting(() => {
  console.warn("⚠️ Reconnecting...");
});

connection.onreconnected(() => {
  console.log("✅ Reconnected");
});

// Start connection
await connection.start();

// Subscribe to devices
await connection.invoke("SubscribeToDevice", "device-guid-1");
await connection.invoke("SubscribeToDevice", "device-guid-2");

// Later: unsubscribe
// await connection.invoke("UnsubscribeFromDevice", "device-guid-1");
```

---

## Configuration

### Database Connection

**File**: `appsettings.json` / `appsettings.Development.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=synapse_iiot;User=root;Password=yourpassword;"
  }
}
```

### JWT Settings

```json
{
  "JwtSettings": {
    "SecretKey": "your-super-secret-key-min-32-characters-long",
    "Issuer": "SynapseIIoT",
    "Audience": "SynapseIIoTClient",
    "ExpiryInMinutes": 60
  }
}
```

### SignalR Settings

```json
{
  "SignalRSettings": {
    "GroupPrefix": {
      "Device": "synapse_device_",
      "StorageFlow": "synapse_flow_",
      "MasterTable": "synapse_table_"
    },
    "EnableDetailedErrors": false,
    "ClientTimeoutInterval": "00:01:00",
    "KeepAliveInterval": "00:00:30"
  }
}
```

### CORS Configuration

**File**: `Api/Program.cs`

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```

---

## Frontend Integration Guide

### Complete Authentication Flow

```javascript
// auth.js - Authentication Service
class AuthService {
  constructor(baseURL = "http://localhost:5000") {
    this.baseURL = baseURL;
    this.currentUser = null;
  }

  async register(username, email, password, role = "USER") {
    const response = await fetch(`${this.baseURL}/api/auth/register`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify({ username, email, password, role }),
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message || "Registration failed");
    }

    return result.data;
  }

  async login(email, password) {
    const response = await fetch(`${this.baseURL}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify({ email, password }),
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message || "Login failed");
    }

    this.currentUser = result.data;

    // Store user info in localStorage (optional, token is in HTTP-only cookie)
    localStorage.setItem("user", JSON.stringify(result.data));

    return result.data;
  }

  async getCurrentUser() {
    if (this.currentUser) return this.currentUser;

    const response = await fetch(`${this.baseURL}/api/auth/me`, {
      method: "GET",
      credentials: "include",
    });

    if (!response.ok) {
      return null;
    }

    const result = await response.json();
    this.currentUser = result.data;
    localStorage.setItem("user", JSON.stringify(result.data));

    return result.data;
  }

  async logout() {
    const response = await fetch(`${this.baseURL}/api/auth/logout`, {
      method: "POST",
      credentials: "include",
    });

    this.currentUser = null;
    localStorage.removeItem("user");

    return response.ok;
  }

  isAuthenticated() {
    return this.currentUser !== null || localStorage.getItem("user") !== null;
  }

  getUserRole() {
    const user =
      this.currentUser || JSON.parse(localStorage.getItem("user") || "null");
    return user?.role || null;
  }
}

const authService = new AuthService();

// Usage in Login Component
async function handleLogin(email, password) {
  try {
    const user = await authService.login(email, password);
    console.log("Logged in as:", user.username);
    window.location.href = "/dashboard";
  } catch (error) {
    alert(`Login failed: ${error.message}`);
  }
}
```

### Complete Device Management Integration

```javascript
// deviceService.js
class DeviceService {
  constructor(baseURL = "http://localhost:5000") {
    this.baseURL = baseURL;
  }

  async getDevices(page = 1, pageSize = 10, searchTerm = "", protocol = null) {
    const params = new URLSearchParams({
      page: page.toString(),
      pageSize: pageSize.toString(),
    });

    if (searchTerm) params.append("searchTerm", searchTerm);
    if (protocol) params.append("protocol", protocol);

    const response = await fetch(`${this.baseURL}/api/device?${params}`, {
      method: "GET",
      credentials: "include",
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message);
    }

    return {
      devices: result.data,
      paging: result.paging,
    };
  }

  async getDeviceById(deviceId) {
    const response = await fetch(`${this.baseURL}/api/device/${deviceId}`, {
      method: "GET",
      credentials: "include",
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message);
    }

    return result.data;
  }

  async createDevice(deviceData) {
    const response = await fetch(`${this.baseURL}/api/device`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify(deviceData),
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message);
    }

    return result.data;
  }

  async updateDevice(deviceId, deviceData) {
    const response = await fetch(`${this.baseURL}/api/device/${deviceId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify(deviceData),
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message);
    }

    return result.data;
  }

  async deleteDevice(deviceId) {
    const response = await fetch(`${this.baseURL}/api/device/${deviceId}`, {
      method: "DELETE",
      credentials: "include",
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message);
    }

    return true;
  }

  async testHttpConnection(url, method, headers = {}, body = null) {
    const response = await fetch(`${this.baseURL}/api/device/http-test`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify({ url, method, headers, body }),
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message);
    }

    return result.data;
  }
}

const deviceService = new DeviceService();

// React Component Example
function DeviceList() {
  const [devices, setDevices] = React.useState([]);
  const [loading, setLoading] = React.useState(true);
  const [page, setPage] = React.useState(1);
  const [totalPages, setTotalPages] = React.useState(1);

  React.useEffect(() => {
    loadDevices();
  }, [page]);

  async function loadDevices() {
    setLoading(true);
    try {
      const { devices, paging } = await deviceService.getDevices(page, 10);
      setDevices(devices);
      setTotalPages(paging.totalPages);
    } catch (error) {
      alert(`Failed to load devices: ${error.message}`);
    } finally {
      setLoading(false);
    }
  }

  async function handleDeleteDevice(deviceId) {
    if (!confirm("Are you sure you want to delete this device?")) return;

    try {
      await deviceService.deleteDevice(deviceId);
      alert("Device deleted successfully");
      loadDevices(); // Reload list
    } catch (error) {
      alert(`Delete failed: ${error.message}`);
    }
  }

  if (loading) return <div>Loading...</div>;

  return (
    <div>
      <h2>Devices</h2>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Protocol</th>
            <th>Status</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {devices.map((device) => (
            <tr key={device.id}>
              <td>{device.name}</td>
              <td>{device.protocol}</td>
              <td>{device.isEnabled ? "✅ Enabled" : "❌ Disabled"}</td>
              <td>
                <button
                  onClick={() =>
                    (window.location.href = `/device/edit/${device.id}`)
                  }
                >
                  Edit
                </button>
                <button onClick={() => handleDeleteDevice(device.id)}>
                  Delete
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <div>
        <button
          onClick={() => setPage((p) => Math.max(1, p - 1))}
          disabled={page === 1}
        >
          Previous
        </button>
        <span>
          Page {page} of {totalPages}
        </span>
        <button
          onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
          disabled={page === totalPages}
        >
          Next
        </button>
      </div>
    </div>
  );
}
```

### Complete Storage Flow Integration

```javascript
// storageFlowService.js
class StorageFlowService {
  constructor(baseURL = "http://localhost:5000") {
    this.baseURL = baseURL;
  }

  async discoverFields(deviceId) {
    const response = await fetch(
      `${this.baseURL}/api/storage-flow/discover-fields`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ deviceId }),
      },
    );

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message);
    }

    return result.data;
  }

  async createStorageFlow(flowData) {
    const response = await fetch(`${this.baseURL}/api/storage-flow`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify(flowData),
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message);
    }

    return result.data;
  }

  async getStorageFlows(page = 1, pageSize = 10) {
    const params = new URLSearchParams({
      page: page.toString(),
      pageSize: pageSize.toString(),
    });

    const response = await fetch(`${this.baseURL}/api/storage-flow?${params}`, {
      method: "GET",
      credentials: "include",
    });

    const result = await response.json();

    if (!response.ok) {
      throw new Error(result.message);
    }

    return {
      flows: result.data,
      paging: result.paging,
    };
  }
}

const storageFlowService = new StorageFlowService();

// Usage: Create Storage Flow with Field Discovery
async function createFlowWithDiscovery() {
  const deviceId = "device-guid-1";
  const masterTableId = "table-guid-1";

  try {
    // Step 1: Discover available fields from device
    const discoveredFields = await storageFlowService.discoverFields(deviceId);

    console.log("Discovered fields:", discoveredFields);
    // [
    //   { path: "$.data.temperature", type: "FLOAT", sampleValue: "25.5" },
    //   { path: "$.data.humidity", type: "FLOAT", sampleValue: "60.2" }
    // ]

    // Step 2: Let user map discovered fields to master table fields
    // (In real app, show UI for user to select mappings)
    const mappings = [
      {
        masterTableFieldId: "field-temp-guid",
        sourcePath: "$.data.temperature",
      },
      {
        masterTableFieldId: "field-humidity-guid",
        sourcePath: "$.data.humidity",
      },
    ];

    // Step 3: Create storage flow
    const flowData = {
      name: "Auto Sensor Storage",
      description: "Automatically store sensor readings",
      isActive: true,
      storageInterval: 10000, // 10 seconds
      masterTableId: masterTableId,
      deviceIds: [deviceId],
      mappings: mappings,
    };

    const createdFlow = await storageFlowService.createStorageFlow(flowData);

    console.log("Storage flow created:", createdFlow);
    alert("Storage flow created successfully!");
  } catch (error) {
    console.error("Flow creation failed:", error);
    alert(`Failed: ${error.message}`);
  }
}
```

### Complete SignalR Real-Time Integration

```javascript
// realtimeService.js
import * as signalR from "@microsoft/signalr";

class RealtimeService {
  constructor(baseURL = "http://localhost:5000") {
    this.baseURL = baseURL;
    this.connection = null;
    this.isConnected = false;
    this.subscribers = new Map(); // deviceId -> callback[]
  }

  async connect() {
    if (this.connection) {
      console.warn("Already connected or connecting");
      return;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${this.baseURL}/deviceHub`, {
        withCredentials: true,
        skipNegotiation: false,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          if (retryContext.elapsedMilliseconds < 60000) {
            return Math.min(
              1000 * (retryContext.previousRetryCount + 1),
              10000,
            );
          } else {
            return null; // Stop retrying after 1 minute
          }
        },
      })
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Set up event handlers
    this.connection.on("Connected", (data) => {
      console.log("✅ SignalR Connected:", data);
      this.isConnected = true;
    });

    this.connection.on("Subscribed", (data) => {
      console.log("📡 Subscribed to device:", data.deviceId);
    });

    this.connection.on("ReceiveDeviceData", (data) => {
      console.log("📊 Real-time device data:", data);

      // Notify all subscribers for this device
      const callbacks = this.subscribers.get(data.deviceId) || [];
      callbacks.forEach((callback) => {
        try {
          callback(data);
        } catch (error) {
          console.error("Subscriber callback error:", error);
        }
      });
    });

    this.connection.on("Unsubscribed", (data) => {
      console.log("🔕 Unsubscribed from device:", data.deviceId);
    });

    this.connection.onreconnecting(() => {
      console.warn("⚠️ SignalR Reconnecting...");
      this.isConnected = false;
    });

    this.connection.onreconnected(() => {
      console.log("✅ SignalR Reconnected");
      this.isConnected = true;

      // Re-subscribe to all devices
      this.subscribers.forEach((_, deviceId) => {
        this.subscribeToDevice(deviceId);
      });
    });

    this.connection.onclose(() => {
      console.error("❌ SignalR Connection Closed");
      this.isConnected = false;
    });

    // Start connection
    try {
      await this.connection.start();
      console.log("SignalR connection established");
    } catch (error) {
      console.error("SignalR connection failed:", error);
      throw error;
    }
  }

  async disconnect() {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
      this.isConnected = false;
    }
  }

  async subscribeToDevice(deviceId, callback) {
    if (!this.isConnected) {
      throw new Error("Not connected to SignalR hub");
    }

    // Add callback to subscribers
    if (!this.subscribers.has(deviceId)) {
      this.subscribers.set(deviceId, []);
    }

    if (callback) {
      this.subscribers.get(deviceId).push(callback);
    }

    // Subscribe via SignalR
    try {
      await this.connection.invoke("SubscribeToDevice", deviceId);
    } catch (error) {
      console.error(`Failed to subscribe to device ${deviceId}:`, error);
      throw error;
    }
  }

  async unsubscribeFromDevice(deviceId) {
    if (!this.isConnected) {
      return;
    }

    this.subscribers.delete(deviceId);

    try {
      await this.connection.invoke("UnsubscribeFromDevice", deviceId);
    } catch (error) {
      console.error(`Failed to unsubscribe from device ${deviceId}:`, error);
    }
  }

  async getConnectionInfo() {
    if (!this.isConnected) {
      throw new Error("Not connected");
    }

    await this.connection.invoke("GetConnectionInfo");
  }
}

const realtimeService = new RealtimeService();

// React Component Example: Real-time Device Dashboard
function DeviceDashboard({ deviceId }) {
  const [deviceData, setDeviceData] = React.useState(null);
  const [status, setStatus] = React.useState("disconnected");

  React.useEffect(() => {
    let mounted = true;

    async function init() {
      try {
        setStatus("connecting");

        // Connect to SignalR
        await realtimeService.connect();

        // Subscribe to device with callback
        await realtimeService.subscribeToDevice(deviceId, (data) => {
          if (mounted) {
            setDeviceData(data);
            setStatus("connected");
          }
        });
      } catch (error) {
        console.error("Failed to initialize real-time connection:", error);
        setStatus("error");
      }
    }

    init();

    // Cleanup on unmount
    return () => {
      mounted = false;
      realtimeService.unsubscribeFromDevice(deviceId);
    };
  }, [deviceId]);

  return (
    <div>
      <h2>Device Dashboard</h2>
      <p>Status: {status}</p>

      {deviceData && (
        <div>
          <h3>{deviceData.deviceName}</h3>
          <p>Protocol: {deviceData.protocol}</p>
          <p>Timestamp: {new Date(deviceData.timestamp).toLocaleString()}</p>
          <p>Status: {deviceData.status}</p>

          {deviceData.data && (
            <pre>{JSON.stringify(deviceData.data, null, 2)}</pre>
          )}
        </div>
      )}
    </div>
  );
}
```

### Error Handling Best Practices

```javascript
// apiClient.js - Centralized API client with error handling
class APIClient {
  constructor(baseURL = "http://localhost:5000") {
    this.baseURL = baseURL;
  }

  async request(endpoint, options = {}) {
    const url = `${this.baseURL}${endpoint}`;
    const config = {
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        ...options.headers,
      },
      ...options,
    };

    try {
      const response = await fetch(url, config);
      const data = await response.json();

      if (!response.ok) {
        // Handle different error statuses
        switch (response.status) {
          case 400:
            throw new ValidationError(data.message, data.error);
          case 401:
            // Redirect to login
            window.location.href = "/login";
            throw new AuthenticationError(
              "Session expired. Please login again.",
            );
          case 403:
            throw new AuthorizationError(
              "You do not have permission to perform this action.",
            );
          case 404:
            throw new NotFoundError(data.message);
          case 500:
            throw new ServerError(
              "Server error occurred. Please try again later.",
            );
          default:
            throw new APIError(data.message || "An error occurred");
        }
      }

      return data;
    } catch (error) {
      if (error instanceof APIError) {
        throw error;
      }

      // Network error
      if (error.name === "TypeError" && error.message.includes("fetch")) {
        throw new NetworkError("Network error. Please check your connection.");
      }

      throw error;
    }
  }

  get(endpoint, params = {}) {
    const query = new URLSearchParams(params).toString();
    const url = query ? `${endpoint}?${query}` : endpoint;
    return this.request(url, { method: "GET" });
  }

  post(endpoint, body = {}) {
    return this.request(endpoint, {
      method: "POST",
      body: JSON.stringify(body),
    });
  }

  put(endpoint, body = {}) {
    return this.request(endpoint, {
      method: "PUT",
      body: JSON.stringify(body),
    });
  }

  delete(endpoint) {
    return this.request(endpoint, { method: "DELETE" });
  }
}

// Custom Error Classes
class APIError extends Error {
  constructor(message) {
    super(message);
    this.name = "APIError";
  }
}

class ValidationError extends APIError {
  constructor(message, details) {
    super(message);
    this.name = "ValidationError";
    this.details = details;
  }
}

class AuthenticationError extends APIError {
  constructor(message) {
    super(message);
    this.name = "AuthenticationError";
  }
}

class AuthorizationError extends APIError {
  constructor(message) {
    super(message);
    this.name = "AuthorizationError";
  }
}

class NotFoundError extends APIError {
  constructor(message) {
    super(message);
    this.name = "NotFoundError";
  }
}

class ServerError extends APIError {
  constructor(message) {
    super(message);
    this.name = "ServerError";
  }
}

class NetworkError extends Error {
  constructor(message) {
    super(message);
    this.name = "NetworkError";
  }
}

// Usage
const apiClient = new APIClient();

async function example() {
  try {
    const result = await apiClient.get("/api/device", {
      page: 1,
      pageSize: 10,
    });
    console.log("Devices:", result.data);
  } catch (error) {
    if (error instanceof ValidationError) {
      console.error("Validation failed:", error.details);
      alert(`Validation error: ${error.message}`);
    } else if (error instanceof AuthenticationError) {
      console.error("Authentication required");
      // Redirect handled automatically in request()
    } else if (error instanceof NetworkError) {
      console.error("Network issue:", error.message);
      alert("Network error. Please check your connection.");
    } else {
      console.error("Unexpected error:", error);
      alert("An unexpected error occurred");
    }
  }
}
```

---

## How It Works

### Architecture Overview

```
┌─────────────────┐
│   Frontend      │
│  (React/Vue)    │
└────────┬────────┘
         │
         │ HTTP REST API + SignalR WebSocket
         │
┌────────┴────────┐
│   API Layer     │
│  (Controllers)  │
└────────┬────────┘
         │
┌────────┴────────┐
│  Service Layer  │
│  (Business)     │
└────────┬────────┘
         │
┌────────┴────────┐
│ Repository      │
│ (Data Access)   │
└────────┬────────┘
         │
┌────────┴────────┐
│  Database       │
│    (MySQL)      │
└─────────────────┘

Background:
┌──────────────────┐
│ DeviceWorker     │ ──► Poll Devices (HTTP/MQTT/MODBUS/OPCUA)
│ Service          │ ──► Broadcast to SignalR
│                  │ ──► Execute Storage Flows
└──────────────────┘
```

### Data Flow for Storage Flow

1. **Setup Phase**:
   - Admin creates Master Table with fields (e.g., `sensor_data_table`)
   - Physical table is created in MySQL
   - Admin creates Devices (HTTP sensors, MODBUS PLCs, etc.)
   - Admin uses `/discover-fields` to see available data from device
   - Admin creates Storage Flow with field mappings

2. **Runtime Phase**:
   - `DeviceWorkerService` starts background tasks for each enabled device
   - Every `pollingInterval`, worker fetches fresh data from device
   - Data is broadcast via SignalR to subscribed clients
   - Separately, every `storageInterval`, worker executes Storage Flow:
     - Fetches data from all devices in the flow
     - Extracts values using JSONPath (HTTP/MQTT) or Tag names (MODBUS/OPCUA)
     - Inserts row into physical master table

3. **Query Phase**:
   - Data is stored in dynamically created tables
   - Use standard SQL tools to query: `SELECT * FROM sensor_data_table;`
   - Or integrate with BI tools like Grafana, Power BI, etc.

### JSONPath Mapping (HTTP/MQTT)

Given device response:

```json
{
  "status": "ok",
  "data": {
    "sensors": {
      "temp": 25.5,
      "humidity": 60.0
    },
    "location": "Room A"
  }
}
```

Mapping examples:

- `$.status` → "ok"
- `$.data.sensors.temp` → 25.5
- `$.data.sensors.humidity` → 60.0
- `$.data.location` → "Room A"

### Tag-Based Mapping (MODBUS/OPC UA)

For MODBUS/OPCUA devices, you must first create Tag entities with:

- `Name`: Tag identifier (e.g., "Temp_Sensor_1")
- `Address`: Register address or node ID
- `DataType`: Data type of the tag

Then use Tag.Name as `sourcePath` in mapping:

```json
{
  "masterTableFieldId": "field-temperature-guid",
  "sourcePath": "Temp_Sensor_1",
  "tagId": "tag-guid-1"
}
```

### Auto-Restart Mechanism

**Device Changes**:

- When you create/update/delete a device, worker automatically starts/restarts/stops polling
- NO server restart required

**Storage Flow Changes**:

- When you create/update/delete a flow, worker automatically starts/stops flow execution
- NO server restart required

**How**:

- Worker runs a monitoring loop every few seconds
- Queries database for enabled devices and active flows
- Compares with currently running tasks
- Starts new tasks or cancels removed ones

### Security Features

- ✅ JWT authentication with HTTP-only cookies
- ✅ SQL injection prevention with parameterized queries
- ✅ CORS configuration for allowed origins
- ✅ Environment-based SignalR group prefixes
- ✅ Soft delete (no data permanently lost immediately)
- ✅ Role-based authorization (ADMIN/USER/VIEWER)

### Database Schema Highlights

**Dynamic Table Structure**:

When you create a Master Table named "SensorData" with:

- Field: "temperature" (FLOAT)
- Field: "humidity" (FLOAT)
- Field: "status" (STRING)

Physical table created:

```sql
CREATE TABLE sensor_data_table (
  Id CHAR(36) PRIMARY KEY,
  Timestamp DATETIME(6) NOT NULL,
  temperature DOUBLE NULL,
  humidity DOUBLE NULL,
  status VARCHAR(255) NULL
);
```

**Key Tables**:

- `devices` - Device configurations
- `master_tables` - Table definitions
- `master_table_fields` - Field definitions
- `storage_flows` - Flow configurations
- `storage_flow_devices` - Many-to-Many junction
- `storage_flow_mappings` - Field mappings
- `tags` - Tag definitions for MODBUS/OPCUA
- `users` - User accounts

---

## Quick Start Guide

### 1. Run Migration

```powershell
cd Infrastructure
dotnet ef database update --startup-project ..\Api
```

### 2. Start the Application

```powershell
cd Api
dotnet run
```

### 3. Register & Login

```bash
# Register
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","email":"admin@test.com","password":"Pass123!","role":"ADMIN"}'

# Login
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@test.com","password":"Pass123!"}'
```

### 4. Create a Master Table

```bash
curl -X POST http://localhost:5000/api/master-table \
  -H "Content-Type: application/json" \
  -H "Cookie: auth_token=YOUR_TOKEN" \
  -d '{
    "name":"SensorData",
    "tableName":"sensor_data",
    "description":"Sensor storage",
    "isActive":true,
    "fields":[
      {"name":"temperature","dataType":"FLOAT","isEnabled":true},
      {"name":"humidity","dataType":"FLOAT","isEnabled":true}
    ]
  }'
```

### 5. Create a Device

```bash
curl -X POST http://localhost:5000/api/device \
  -H "Content-Type: application/json" \
  -H "Cookie: auth_token=YOUR_TOKEN" \
  -d '{
    "name":"Sensor-001",
    "description":"Test sensor",
    "isEnabled":true,
    "protocol":"HTTP",
    "pollingInterval":5000,
    "connectionConfig":{
      "url":"http://192.168.1.100/api/data",
      "method":"GET"
    }
  }'
```

### 6. Discover Fields

```bash
curl -X POST http://localhost:5000/api/storage-flow/discover-fields \
  -H "Content-Type: application/json" \
  -H "Cookie: auth_token=YOUR_TOKEN" \
  -d '{"deviceId":"YOUR_DEVICE_ID"}'
```

### 7. Create Storage Flow

```bash
curl -X POST http://localhost:5000/api/storage-flow \
  -H "Content-Type: application/json" \
  -H "Cookie: auth_token=YOUR_TOKEN" \
  -d '{
    "name":"Auto Storage",
    "description":"Store sensor data",
    "isActive":true,
    "storageInterval":10000,
    "masterTableId":"YOUR_TABLE_ID",
    "deviceIds":["YOUR_DEVICE_ID"],
    "mappings":[
      {"masterTableFieldId":"FIELD_ID_1","sourcePath":"$.data.temperature"},
      {"masterTableFieldId":"FIELD_ID_2","sourcePath":"$.data.humidity"}
    ]
  }'
```

### 8. Connect via SignalR

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5000/deviceHub", {
    withCredentials: true,
  })
  .build();

await connection.start();
await connection.invoke("SubscribeToDevice", "YOUR_DEVICE_ID");

connection.on("ReceiveDeviceData", (data) => {
  console.log("Real-time data:", data);
});
```

---

## Data Types Reference

### Master Table Field Types

| DataType | MySQL Type   | Example Value          |
| -------- | ------------ | ---------------------- |
| STRING   | VARCHAR(255) | "sensor_01"            |
| INT16    | SMALLINT     | 100                    |
| INT32    | INT          | 1000000                |
| INT64    | BIGINT       | 9223372036854775807    |
| FLOAT    | FLOAT        | 25.5                   |
| DOUBLE   | DOUBLE       | 25.123456789           |
| BOOLEAN  | TINYINT(1)   | true/false (1/0)       |
| DATETIME | DATETIME(6)  | "2026-02-18T10:30:00Z" |

### Protocol Types

| Protocol   | Description              | Connection Config Fields               |
| ---------- | ------------------------ | -------------------------------------- |
| HTTP       | REST API                 | url, method, headers, body             |
| MQTT       | Message broker           | broker, port, topic, username,password |
| MODBUS_TCP | MODBUS over TCP/IP       | host, port, slaveId                    |
| MODBUS_RTU | MODBUS over Serial       | port, baudRate, slaveId                |
| OPC_UA     | OPC Unified Architecture | endpointUrl, username, password        |

### User Roles

| Role   | Description       |
| ------ | ----------------- |
| ADMIN  | Full access       |
| USER   | Read/Write access |
| VIEWER | Read-only access  |

---

## Error Codes

| Status Code | Meaning                              |
| ----------- | ------------------------------------ |
| 200         | Success                              |
| 201         | Created                              |
| 400         | Bad Request (validation error)       |
| 401         | Unauthorized (missing/invalid token) |
| 404         | Not Found                            |
| 500         | Internal Server Error                |

**Example Error Response**:

```json
{
  "status": 404,
  "message": "Device not found"
}
```

---

## Advanced Features

### Custom Storage Interval per Flow

Each Storage Flow can have different `storageInterval`:

- Flow A: Store every 5 seconds
- Flow B: Store every 1 minute
- Flow C: Store every 1 hour

This allows flexible data retention strategies.

### Multiple Devices in One Flow

A single Storage Flow can pull data from multiple devices and merge them into one table row:

```json
{
  "deviceIds": [
    "temp-sensor-guid",
    "humidity-sensor-guid",
    "pressure-sensor-guid"
  ],
  "mappings": [
    { "masterTableFieldId": "temp-field", "sourcePath": "$.temperature" },
    { "masterTableFieldId": "humidity-field", "sourcePath": "$.humidity" },
    { "masterTableFieldId": "pressure-field", "sourcePath": "$.pressure" }
  ]
}
```

Each device is polled, data extracted, and combined into a single INSERT.

### Soft Delete Recovery

All entities use soft delete:

```sql
-- View deleted items
SELECT * FROM devices WHERE DeletedAt IS NOT NULL;

-- Restore manually
UPDATE devices SET DeletedAt = NULL WHERE Id = 'device-guid';
```

### Background Task Monitoring

Check logs to see worker activity:

```
[DeviceWorkerService] Polling started for Sensor-001 with interval 5000ms
[DeviceWorkerService] Data sent for device Sensor-001: {"temperature":25.5}
[DeviceWorkerService] Storage flow started: Sensor Data Flow with interval 10000ms
```

---

## Troubleshooting

### SignalR Connection Fails

**Problem**: Client cannot connect to `/deviceHub`

**Solution**:

- Ensure CORS allows your frontend origin
- Check if credentials are included: `withCredentials: true`
- Verify SignalR endpoint is registered in Program.cs: `app.MapHub<DeviceDataHub>("/deviceHub")`

### Device Polling Not Starting

**Problem**: Device created but no data in SignalR

**Solution**:

- Ensure `IsEnabled = true`
- Check device connection config is valid
- View logs for errors
- Restart application to force worker refresh

### Storage Flow Not Inserting Data

**Problem**: Flow is active but table remains empty

**Solution**:

- Ensure Master Table `IsActive = true`
- Ensure Storage Flow `IsActive = true`
- Check that physical table exists in database
- Verify mappings have correct `sourcePath`
- Check device is returning data (`/discover-fields`)

### Field Discovery Returns Empty

**Problem**: `/discover-fields` returns no fields

**Solution**:

- Test device connection first with `/api/device/http-test`
- Check device is enabled and responding
- For MODBUS/OPCUA: Ensure Tags are created for the device

---

## Support & Contribution

For questions, feature requests, or bug reports, please document for development.

**Implementation Complete! 🎉**

---

**Version**: 1.0.0  
**Last Updated**: 2026-02-19  
**License**: Proprietary
