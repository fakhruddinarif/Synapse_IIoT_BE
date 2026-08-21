using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreatePostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dipasang lebih dulu: create_hypertable() di bawah membutuhkan ekstensi ini, dan
            // memasangnya sebelum ada tabel sama sekali membuat urutannya tidak bergantung pada
            // tabel mana yang dibuat lebih dulu oleh generator EF.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;");

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Protocol = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "HTTP"),
                    ConnectionConfigJson = table.Column<string>(type: "jsonb", nullable: false),
                    PollingInterval = table.Column<int>(type: "integer", nullable: false, defaultValue: 1000),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    FieldName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)"),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileMetadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MasterTables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TableName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterTables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Role = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "VIEWER"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DataType = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "FLOAT"),
                    AccessMode = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "READONLY"),
                    IsScaled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RawMin = table.Column<double>(type: "double precision", nullable: true),
                    RawMax = table.Column<double>(type: "double precision", nullable: true),
                    EuMin = table.Column<double>(type: "double precision", nullable: true),
                    EuMax = table.Column<double>(type: "double precision", nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentRawValue = table.Column<double>(type: "double precision", nullable: true),
                    CurrentEngValue = table.Column<double>(type: "double precision", nullable: true),
                    ValueUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceTopic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ScanIntervalMs = table.Column<int>(type: "integer", nullable: false),
                    StoreMode = table.Column<int>(type: "integer", nullable: false),
                    DeadbandAbs = table.Column<double>(type: "double precision", nullable: true),
                    DeadbandPct = table.Column<double>(type: "double precision", nullable: true),
                    MaxStoreGapMs = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OpcUaNodeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MasterTableFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MasterTableId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DataType = table.Column<string>(type: "varchar(50)", nullable: false, defaultValue: "STRING"),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterTableFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MasterTableFields_MasterTables_MasterTableId",
                        column: x => x.MasterTableId,
                        principalTable: "MasterTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorageFlows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    StorageInterval = table.Column<int>(type: "integer", nullable: false, defaultValue: 1000),
                    MasterTableId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageFlows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageFlows_MasterTables_MasterTableId",
                        column: x => x.MasterTableId,
                        principalTable: "MasterTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    OldValues = table.Column<string>(type: "jsonb", nullable: true),
                    NewValues = table.Column<string>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TagHistories",
                columns: table => new
                {
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTs = table.Column<DateTime>(type: "timestamp(6) with time zone", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayTs = table.Column<DateTime>(type: "timestamp(6) with time zone", nullable: false),
                    NumericValue = table.Column<double>(type: "double precision", nullable: true),
                    BoolValue = table.Column<bool>(type: "boolean", nullable: true),
                    TextValue = table.Column<string>(type: "text", maxLength: 500, nullable: true),
                    RawValue = table.Column<double>(type: "double precision", nullable: true),
                    Quality = table.Column<byte>(type: "smallint", nullable: false),
                    Note = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagHistories", x => new { x.TagId, x.SourceTs });
                    table.ForeignKey(
                        name: "FK_TagHistories_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorageFlowDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageFlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageFlowDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageFlowDevices_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageFlowDevices_StorageFlows_StorageFlowId",
                        column: x => x.StorageFlowId,
                        principalTable: "StorageFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorageFlowMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageFlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    MasterTableFieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageFlowMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageFlowMappings_MasterTableFields_MasterTableFieldId",
                        column: x => x.MasterTableFieldId,
                        principalTable: "MasterTableFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorageFlowMappings_StorageFlows_StorageFlowId",
                        column: x => x.StorageFlowId,
                        principalTable: "StorageFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_DeletedAt",
                table: "FileMetadata",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_EntityType_EntityId_FieldName",
                table: "FileMetadata",
                columns: new[] { "EntityType", "EntityId", "FieldName" });

            migrationBuilder.CreateIndex(
                name: "IX_MasterTableFields_MasterTableId",
                table: "MasterTableFields",
                column: "MasterTableId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageFlowDevices_DeviceId",
                table: "StorageFlowDevices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageFlowDevices_StorageFlowId_DeviceId",
                table: "StorageFlowDevices",
                columns: new[] { "StorageFlowId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorageFlowMappings_MasterTableFieldId",
                table: "StorageFlowMappings",
                column: "MasterTableFieldId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageFlowMappings_StorageFlowId",
                table: "StorageFlowMappings",
                column: "StorageFlowId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageFlows_MasterTableId",
                table: "StorageFlows",
                column: "MasterTableId");

            migrationBuilder.CreateIndex(
                name: "IX_TagHistories_DeviceId_SourceTs",
                table: "TagHistories",
                columns: new[] { "DeviceId", "SourceTs" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_DeviceId",
                table: "Tags",
                column: "DeviceId");

            /* =====================================================================
               TimescaleDB — TagHistories menjadi hypertable.

               Ini alasan sebenarnya migrasi ini dijalankan: tabel yang akan berisi ratusan
               juta baris dipartisi otomatis per rentang waktu ("chunk"), sehingga sebuah
               kueri yang dibatasi rentang tanggal hanya menyentuh chunk yang relevan alih-alih
               memindai seluruh tabel, dan chunk lama bisa dikompresi tanpa mengganggu tulisan
               ke chunk yang sedang aktif.

               PK komposit (TagId, SourceTs) BUKAN kebetulan cocok dengan syarat Timescale
               ("unique index pada hypertable wajib memuat kolom partisinya") — desain
               TagHistory.cs memang dibuat mengikuti syarat ini dari awal, lihat catatan di
               kelas itu.
            ===================================================================== */
            migrationBuilder.Sql(
                "SELECT create_hypertable('\"TagHistories\"', by_range('SourceTs'), if_not_exists => TRUE);");

            // Kompresi: AMAN dan tidak destruktif — chunk yang dikompresi tetap terbaca penuh
            // lewat kueri biasa, hanya lebih hemat penyimpanan dan lebih cepat untuk scan
            // rentang lebar. segmentby TagId karena hampir semua kueri operasional membatasi
            // pada satu tag; orderby SourceTs karena itulah urutan baris ditulis.
            //
            // TIDAK ada retention policy (penghapusan otomatis) di sini dengan sengaja — itu
            // destruktif dan permanen, dan PRD.md pertanyaan Q2 (tag mana yang butuh retensi
            // data mentah >90 hari) belum punya jawaban. Kompresi boleh otomatis; penghapusan
            // menunggu keputusan sadar.
            migrationBuilder.Sql(
                "ALTER TABLE \"TagHistories\" SET (" +
                "timescaledb.compress, " +
                "timescaledb.compress_segmentby = '\"TagId\"', " +
                "timescaledb.compress_orderby = '\"SourceTs\"'" +
                ");");

            migrationBuilder.Sql(
                "SELECT add_compression_policy('\"TagHistories\"', INTERVAL '7 days');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "FileMetadata");

            migrationBuilder.DropTable(
                name: "StorageFlowDevices");

            migrationBuilder.DropTable(
                name: "StorageFlowMappings");

            migrationBuilder.DropTable(
                name: "TagHistories");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "MasterTableFields");

            migrationBuilder.DropTable(
                name: "StorageFlows");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "MasterTables");

            migrationBuilder.DropTable(
                name: "Devices");
        }
    }
}
