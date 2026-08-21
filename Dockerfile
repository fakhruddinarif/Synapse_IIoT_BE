# ---------------------------------------------------------------------------
# Gateway Synapse IIoT — image runtime.
#
# Dua tahap: SDK untuk memulihkan paket dan menerbitkan, runtime untuk menjalankan.
# Image SDK ukurannya ratusan megabyte dan memuat compiler; mengirimkannya ke
# perangkat gateway di pabrik berarti membawa alat yang tidak dipakai sekaligus
# memperluas permukaan serangan.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Berkas proyek disalin lebih dulu, terpisah dari kode. Lapisan restore hanya
# dibangun ulang saat dependensi berubah — bukan setiap kali satu baris kode
# disentuh, yang akan membuat setiap build mengunduh seluruh paket lagi.
COPY Synapse_IIoT_BE.slnx ./
COPY Core/Core.csproj Core/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
COPY Api/Api.csproj Api/
RUN dotnet restore Api/Api.csproj

COPY . .
RUN dotnet publish Api/Api.csproj -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl dipasang khusus untuk healthcheck: image aspnet tidak membawa curl maupun
# wget, dan healthcheck yang tidak bisa dijalankan membuat kontainer selamanya
# berstatus "starting" — orkestrator menunggu sesuatu yang tidak akan pernah datang.
RUN apt-get update \
	&& apt-get install -y --no-install-recommends curl \
	&& rm -rf /var/lib/apt/lists/*

# Direktori data akuisisi dibuat di image, BUKAN dibiarkan dibuat saat runtime.
# Volume bernama mewarisi pemilik direktori dari image saat pertama dipasang;
# tanpa langkah ini volume menjadi milik root, proses non-root gagal menulis WAL,
# dan gateway kehilangan durabilitasnya justru pada percobaan pertama.
RUN mkdir -p /var/lib/synapse/acquisition /app/wwwroot/uploads

COPY --from=build /app/publish ./

# Berjalan sebagai non-root. Gateway ini membuka koneksi ke jaringan OT; proses
# yang berjalan sebagai root di sana adalah satu kerentanan dari kendali penuh.
RUN useradd --system --uid 10001 --home /app synapse \
	&& chown -R synapse:synapse /app /var/lib/synapse
USER synapse

# Kestrel di kontainer default ke 8080 (bukan 5000) sejak .NET 8.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
	DOTNET_gcServer=1 \
	TZ=UTC

# Liveness saja — TIDAK menyentuh database. Lihat catatan di /health/live:
# database mati bukan alasan membunuh gateway yang sedang menampung data di WAL.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
	CMD curl -fsS http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]
