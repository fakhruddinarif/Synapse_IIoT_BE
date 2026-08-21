using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTagAcquisitionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StorageFlowMappings_Tags_TagId",
                table: "StorageFlowMappings");

            migrationBuilder.DropIndex(
                name: "IX_StorageFlowMappings_TagId",
                table: "StorageFlowMappings");

            migrationBuilder.DropColumn(
                name: "TagId",
                table: "StorageFlowMappings");

            migrationBuilder.AddColumn<double>(
                name: "DeadbandAbs",
                table: "Tags",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DeadbandPct",
                table: "Tags",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Tags",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxStoreGapMs",
                table: "Tags",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScanIntervalMs",
                table: "Tags",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceTopic",
                table: "Tags",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreMode",
                table: "Tags",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Action = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OldValues = table.Column<string>(type: "json", nullable: true),
                    NewValues = table.Column<string>(type: "json", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "DeadbandAbs",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "DeadbandPct",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "MaxStoreGapMs",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "ScanIntervalMs",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "SourceTopic",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "StoreMode",
                table: "Tags");

            migrationBuilder.AddColumn<Guid>(
                name: "TagId",
                table: "StorageFlowMappings",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorageFlowMappings_TagId",
                table: "StorageFlowMappings",
                column: "TagId");

            migrationBuilder.AddForeignKey(
                name: "FK_StorageFlowMappings_Tags_TagId",
                table: "StorageFlowMappings",
                column: "TagId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
