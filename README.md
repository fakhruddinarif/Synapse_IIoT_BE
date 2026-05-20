# Synapse IIoT Backend

Backend API untuk sistem Industrial Internet of Things (IIoT) berbasis ASP.NET Core (.NET 10) dengan Clean Architecture. Fokus pada manajemen device, streaming data real-time, dan penyimpanan terstruktur.

## Tech Stack

- .NET 10 (ASP.NET Core Web API)
- Entity Framework Core (MySQL)
- SignalR (real-time streaming)
- JWT Authentication (HTTP-only cookie)
- Rate Limiting + Audit Logging Middleware

## Fitur Utama

- Manajemen device dengan protokol: HTTP, MQTT, Modbus TCP/RTU, OPC UA
- Tag management dengan data type dan scaling
- Master table + field dinamis
- Storage flow dan mapping data
- File upload (single, multiple, per-entity field)
- Background worker untuk polling device
- Audit log ke database

## Prerequisites

- .NET 10 SDK
- MySQL 8.0+
- Git
- IDE: Visual Studio 2022 atau VS Code

Opsional:

- Docker + Docker Compose (untuk MySQL dan RabbitMQ lokal)

## Quick Start

### 1. Clone Repository

```bash
git clone https://github.com/fakhruddinarif/Synapse_IIoT_BE.git
cd Synapse_IIoT_BE
```

### 2. Jalankan Infrastruktur (Opsional, via Docker)

```bash
docker compose up -d
```

Docker Compose menyediakan MySQL dan RabbitMQ. Pastikan connection string di konfigurasi aplikasi sesuai kredensial docker-compose.yml.

### 3. Konfigurasi Aplikasi

Edit Api/appsettings.json (atau appsettings.Development.json):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=db_synapse_iiot;User=root;Password=YOUR_MYSQL_PASSWORD;"
  },
  "JwtSettings": {
    "Secret": "CHANGE_THIS_TO_YOUR_OWN_SECRET_KEY_MIN_32_CHARS!",
    "Issuer": "SynapseIIoT",
    "Audience": "SynapseIIoT",
    "ExpirationInMinutes": 60
  },
  "SignalRSettings": {
    "EnableDetailedErrors": false,
    "ClientTimeoutInterval": 60,
    "KeepAliveInterval": 30
  },
  "FileUploadSettings": {
    "UploadPath": "wwwroot/uploads",
    "BaseUrl": "http://localhost:5009",
    "MaxFileSizeInBytes": "10485760"
  }
}
```

Catatan:

- DefaultConnection wajib mengarah ke database MySQL.
- JwtSettings.Secret minimal 32 karakter.
- FileUploadSettings.BaseUrl sebaiknya sama dengan host API yang berjalan.

### 4. Restore Dependencies

```bash
dotnet restore
```

### 5. Install EF Core Tools (Jika belum)

```bash
dotnet tool install --global dotnet-ef
```

### 6. Jalankan Migration

```bash
dotnet ef database update --project Infrastructure --startup-project Api
```

### 7. Build

```bash
dotnet build
```

## Menjalankan Project

### Development Mode

```bash
dotnet run --project Api
```

### Hot Reload

```bash
dotnet watch run --project Api
```

### Visual Studio

1. Buka solution file Synapse_IIoT_BE.slnx
2. Set Api sebagai Startup Project
3. Tekan F5

## Akses Aplikasi

- HTTP: http://localhost:5009
- HTTPS: https://localhost:7018
- OpenAPI (Development): http://localhost:5009/openapi/v1.json

Port dapat diubah di Api/Properties/launchSettings.json.

## Autentikasi

Login menghasilkan JWT yang disimpan di cookie bernama JWT-TOKEN (HTTP-only). Untuk akses dari frontend browser, pastikan request menggunakan credentials: "include".

Endpoint:

- POST /api/auth/register
- POST /api/auth/login
- GET /api/auth/info
- POST /api/auth/logout

## Ringkasan Endpoint

- Device: /api/device, /api/device/{id}, /api/device/http-test, /api/device/test-http-connection
- Tags: /api/tags, /api/tags/{id}, /api/tags/device/{deviceId}
- Master Tables: /api/master-tables, /api/master-tables/{id}, /api/master-tables/{masterTableId}/fields
- Storage Flow: /api/storage-flow, /api/storage-flow/{id}, /api/storage-flow/discover-fields
- File Upload: /api/file/upload, /api/file/upload-multiple, /api/file/upload-field, /api/file/delete, /api/file/config
- SignalR Hub: /signalr/device-hub

Sumber kebenaran utama untuk rute adalah OpenAPI (Development) karena beberapa dokumen/collection bisa tertinggal.

## Rate Limiting

- Default limiter: 100 request per 1 menit
- Login limiter: 5 request per 15 menit

## Testing API

- Postman collection: Synapse_IIoT_BE.postman_collection.json
- Dokumentasi: Synapse_Backend_APIs.md

Untuk Postman, set baseUrl ke http://localhost:5009/api.

## Database Migration Lainnya

```bash
dotnet ef migrations add <NamaMigration> --project Infrastructure --startup-project Api
dotnet ef migrations list --project Infrastructure --startup-project Api
dotnet ef migrations remove --project Infrastructure --startup-project Api
dotnet ef database update <NamaMigration> --project Infrastructure --startup-project Api
```

## Database Tables

Tabel yang didefinisikan di AppDbContext beserta field utama dan kegunaannya.

| Table               | Fields (type)                                                                                                                                                                                                                                                                                                                                                                                          | Kegunaan                                                            |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------- |
| Users               | Id (Guid), Username (varchar(100)), PasswordHash (varchar(255)), Role (varchar(50) enum), CreatedAt (datetime), UpdatedAt (datetime?), DeletedAt (datetime?)                                                                                                                                                                                                                                           | Data akun dan peran pengguna (soft delete).                         |
| Devices             | Id (Guid), Name (varchar(100)), Description (varchar(255)?), IsEnabled (bool), Protocol (varchar(50) enum), ConnectionConfigJson (json), PollingInterval (int), CreatedAt (datetime), UpdatedAt (datetime?), DeletedAt (datetime?)                                                                                                                                                                     | Master data device dan konfigurasi koneksi dalam JSON.              |
| Tags                | Id (Guid), DeviceId (Guid), Name (varchar(100)), Address (varchar(100)), DataType (varchar(50) enum), AccessMode (varchar(50) enum), IsScaled (bool), RawMin/RawMax/EuMin/EuMax (double?), Unit (varchar(20)), CurrentRawValue/CurrentEngValue (double?), ValueUpdatedAt (datetime?), IsActive (bool), OpcUaNodeId (varchar(100)?), CreatedAt (datetime), UpdatedAt (datetime?), DeletedAt (datetime?) | Mapping alamat/tag sensor, scaling nilai, dan cache nilai terakhir. |
| MasterTables        | Id (Guid), Name (varchar(200)), TableName (varchar(255)), Description (varchar(255)?), IsActive (bool), CreatedAt (datetime), UpdatedAt (datetime?), DeletedAt (datetime?)                                                                                                                                                                                                                             | Definisi tabel dinamis untuk penyimpanan data hasil mapping.        |
| MasterTableFields   | Id (Guid), MasterTableId (Guid), Name (varchar(255)), DataType (varchar(50) enum), IsEnabled (bool), CreatedAt (datetime), UpdatedAt (datetime?), DeletedAt (datetime?)                                                                                                                                                                                                                                | Definisi kolom untuk MasterTable.                                   |
| StorageFlows        | Id (Guid), Name (varchar(100)), Description (varchar(255)?), IsActive (bool), StorageInterval (int), MasterTableId (Guid), CreatedAt (datetime), UpdatedAt (datetime?), DeletedAt (datetime?)                                                                                                                                                                                                          | Aturan alur penyimpanan data ke MasterTable.                        |
| StorageFlowDevices  | Id (Guid), StorageFlowId (Guid), DeviceId (Guid), CreatedAt (datetime)                                                                                                                                                                                                                                                                                                                                 | Relasi banyak-ke-banyak StorageFlow dan Device.                     |
| StorageFlowMappings | Id (Guid), StorageFlowId (Guid), MasterTableFieldId (Guid), SourcePath (varchar(500)), TagId (Guid?), CreatedAt (datetime), UpdatedAt (datetime?)                                                                                                                                                                                                                                                      | Mapping sumber data ke field target (JSONPath atau Tag).            |
| FileMetadata        | Id (Guid), FileName (varchar(255)), OriginalFileName (varchar(255)), FilePath (varchar(500)), FileSize (bigint), ContentType (varchar(100)), EntityType (varchar(50)), EntityId (Guid?), FieldName (varchar(100)), UploadedAt (datetime), DeletedAt (datetime?)                                                                                                                                        | Metadata file upload untuk entity tertentu.                         |
| AuditLogs           | Id (Guid), UserId (Guid?), Action (varchar(50)), EntityType (varchar(50)), EntityId (Guid?), OldValues (json?), NewValues (json?), IpAddress (varchar(45)?), UserAgent (varchar(500)?), Status (enum/int), ErrorMessage (varchar(500)?), CreatedAt (datetime)                                                                                                                                          | Catatan audit perubahan data dan aktivitas pengguna.                |

## Troubleshooting

### Database Connection Error

Error: Unable to connect to any of the specified MySQL hosts

Solusi:

- Pastikan MySQL Server sudah berjalan
- Cek connection string di appsettings.json
- Cek username dan password MySQL

### Migration Error

Error: Build failed

Solusi:

```bash
dotnet clean
dotnet build
dotnet ef database update --project Infrastructure --startup-project Api
```

### Port Already in Use

Error: Address already in use

Solusi:

- Ubah port di Api/Properties/launchSettings.json
- Hentikan proses yang memakai port tersebut

### JWT Secret Too Short

Error: IDX10603: The algorithm HS256 requires the SecurityKey.KeySize to be greater than 128 bits

Solusi:

- Pastikan JwtSettings.Secret minimal 32 karakter

## Struktur Project

```
Synapse_IIoT_BE/
├── Core/               # Domain layer (Entities, Interfaces, Enums)
├── Infrastructure/     # Data layer (DbContext, Repositories, Services)
├── Api/                # Presentation layer (Controllers, Middleware)
└── README.md
```

## Environment Variables (Production)

```bash
export ConnectionStrings__DefaultConnection="Server=prod-server;Database=db_synapse_iiot;..."
export JwtSettings__Secret="your-production-secret-key"
```

## Contributing

1. Fork repository
2. Buat branch baru (git checkout -b feature/AmazingFeature)
3. Commit changes (git commit -m "Add some AmazingFeature")
4. Push ke branch (git push origin feature/AmazingFeature)
5. Buat Pull Request

## License

This project is licensed under the MIT License.

## Developer

Fakhruddin Arif
GitHub: https://github.com/fakhruddinarif
