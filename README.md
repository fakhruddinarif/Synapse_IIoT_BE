# Synapse IIoT Backend

Backend API untuk sistem Industrial Internet of Things (IIoT) menggunakan ASP.NET Core (.NET 10) dengan arsitektur Clean Architecture.

## 🚀 Tech Stack

- **.NET 10** - Framework utama
- **ASP.NET Core Web API** - REST API
- **Entity Framework Core** - ORM
- **MySQL** - Database
- **JWT Authentication** - Keamanan
- **Rate Limiting** - Pembatasan request
- **CSRF Protection** - Keamanan CSRF

## 📋 Prerequisites

Pastikan sudah terinstall:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [MySQL Server](https://dev.mysql.com/downloads/mysql/) (versi 8.0+)
- [Git](https://git-scm.com/downloads)
- IDE: [Visual Studio 2022](https://visualstudio.microsoft.com/) atau [VS Code](https://code.visualstudio.com/)

## 📦 Installation

### 1. Clone Repository

```bash
git clone https://github.com/fakhruddinarif/Synapse_IIoT_BE.git
cd Synapse_IIoT_BE
```

### 2. Setup Database MySQL

Buat database baru di MySQL:

```sql
CREATE DATABASE synapse_iiot;
```

### 3. Konfigurasi Aplikasi

Edit file `Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=synapse_iiot;User=root;Password=YOUR_MYSQL_PASSWORD;"
  },
  "JwtSettings": {
    "Secret": "CHANGE_THIS_TO_YOUR_OWN_SECRET_KEY_MIN_32_CHARS!",
    "Issuer": "SynapseIIoT",
    "Audience": "SynapseIIoT",
    "ExpirationInMinutes": 60
  }
}
```

**⚠️ PENTING:**

- Ganti `YOUR_MYSQL_PASSWORD` dengan password MySQL Anda
- Ganti `Secret` dengan key rahasia Anda sendiri (minimal 32 karakter)

### 4. Restore Dependencies

```bash
dotnet restore
```

### 5. Install EF Core Tools (Jika belum)

```bash
dotnet tool install --global dotnet-ef
```

### 6. Database Migration

#### Buat Migration (Jika belum ada):

```bash
dotnet ef migrations add InitialCreate --project Infrastructure --startup-project Api
```

#### Apply Migration ke Database:

```bash
dotnet ef database update --project Infrastructure --startup-project Api
```

Perintah ini akan membuat tabel-tabel berikut di database:

- `Users` - Data pengguna
- `Devices` - Data device IIoT
- `Tags` - Data tag sensor

### 7. Update Database (Jika Ada Perubahan Entity)

Ketika menambah tabel baru atau mengubah entity yang sudah ada:

#### Buat Migration Baru:

```bash
dotnet ef migrations add <NamaMigration> --project Infrastructure --startup-project Api
```

Contoh:

```bash
# Menambah tabel baru
dotnet ef migrations add AddAlarmsTable --project Infrastructure --startup-project Api

# Menambah kolom ke tabel existing
dotnet ef migrations add AddEmailToUsers --project Infrastructure --startup-project Api

# Multiple perubahan
dotnet ef migrations add UpdateDatabaseSchema --project Infrastructure --startup-project Api
```

#### Apply Migration ke Database:

```bash
dotnet ef database update --project Infrastructure --startup-project Api
```

#### Perintah Migration Lainnya:

```bash
# Undo migration terakhir (jika salah)
dotnet ef migrations remove --project Infrastructure --startup-project Api

# Lihat list migrations
dotnet ef migrations list --project Infrastructure --startup-project Api

# Rollback ke migration tertentu
dotnet ef database update <NamaMigration> --project Infrastructure --startup-project Api
```

### 8. Build Project

```bash
dotnet build
```

## ▶️ Running Project

### Development Mode (Tanpa Hot Reload)

```bash
dotnet run --project Api
```

Atau dari folder Api:

```bash
cd Api
dotnet run
```

### Development Mode dengan Hot Reload (Rekomendasi)

Gunakan `dotnet watch` agar perubahan code langsung teraplikasi tanpa restart manual:

```bash
cd Api
dotnet watch run
```

**Fitur Hot Reload:**

- ✅ Perubahan code otomatis teraplikasi
- ✅ Tekan `Ctrl+R` untuk force restart
- ✅ Tekan `Ctrl+C` untuk stop aplikasi
- ✅ Mendukung hot reload untuk sebagian besar perubahan code

### Menggunakan Visual Studio

1. Buka solution file `Synapse_IIoT_BE.sln`
2. Set `Api` sebagai Startup Project
3. Tekan `F5` atau klik tombol **Start**

## 🌐 Akses Aplikasi

Setelah running, aplikasi dapat diakses di:

- **HTTP**: `http://localhost:5009` (atau port yang ditampilkan saat aplikasi start)
- **OpenAPI/Swagger** (Development): `http://localhost:5009/openapi/v1.json`

**Catatan**: Port bisa berbeda tergantung konfigurasi di `launchSettings.json`. Cek output terminal untuk melihat port aktual yang digunakan.

## 🧪 Testing API

Gunakan file `Api/Auth.http` untuk testing dengan REST Client di VS Code atau Visual Studio.

### Contoh Testing:

1. **Get CSRF Token** (wajib untuk register/login):

```http
GET http://localhost:5009/api/csrf-token
```

2. **Register User**:

```http
POST http://localhost:5009/api/auth/register
Content-Type: application/json
X-CSRF-TOKEN: <token-from-cookie>

{
  "username": "admin",
  "password": "admin123",
  "role": 2
}
```

3. **Login**:

```http
POST http://localhost:5009/api/auth/login
Content-Type: application/json
X-CSRF-TOKEN: <token-from-cookie>

{
  "username": "admin",
  "password": "admin123"
}
```

## 🔧 Troubleshooting

### Database Connection Error

**Error**: `Unable to connect to any of the specified MySQL hosts`

**Solusi**:

- Pastikan MySQL Server sudah running
- Cek connection string di `appsettings.json`
- Cek username dan password MySQL

### Migration Error

**Error**: `Build failed`

**Solusi**:

```bash
dotnet clean
dotnet build
dotnet ef database update --project Infrastructure --startup-project Api
```

### Port Already in Use

**Error**: `Address already in use`

**Solusi**:

- Ubah port di `Api/Properties/launchSettings.json`
- Atau kill process yang menggunakan port tersebut

### JWT Secret Too Short

**Error**: `IDX10603: The algorithm HS256 requires the SecurityKey.KeySize to be greater than 128 bits`

**Solusi**:

- Pastikan `JwtSettings.Secret` di `appsettings.json` minimal 32 karakter

## 📁 Struktur Project

```
Synapse_IIoT_BE/
├── Core/               # Domain layer (Entities, Interfaces, Enums)
├── Infrastructure/     # Data layer (DbContext, Repositories, Services)
├── Api/                # Presentation layer (Controllers, Middleware)
├── .gitignore
└── README.md
```

## 🔐 Security Features

- **JWT Authentication** dengan HTTP-Only Cookie
- **CSRF Protection** menggunakan Anti-forgery Token
- **Rate Limiting** (10 requests per menit)
- **Password Hashing** untuk keamanan data user

## 📝 Environment Variables (Production)

Untuk production, gunakan environment variables atau Azure Key Vault:

```bash
export ConnectionStrings__DefaultConnection="Server=prod-server;Database=synapse_iiot;..."
export JwtSettings__Secret="your-production-secret-key"
```

## 🤝 Contributing

1. Fork repository
2. Buat branch baru (`git checkout -b feature/AmazingFeature`)
3. Commit changes (`git commit -m 'Add some AmazingFeature'`)
4. Push ke branch (`git push origin feature/AmazingFeature`)
5. Buat Pull Request

## 📄 License

This project is licensed under the MIT License.

## 👨‍💻 Developer

**Fakhruddin Arif**

- GitHub: [@fakhruddinarif](https://github.com/fakhruddinarif)

---

**Note**: Jangan lupa untuk mengganti semua placeholder seperti password dan secret key sebelum deployment ke production!
