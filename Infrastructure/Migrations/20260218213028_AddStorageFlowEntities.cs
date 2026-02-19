using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageFlowEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StorageFlow_MasterTables_MasterTableId",
                table: "StorageFlow");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StorageFlow",
                table: "StorageFlow");

            migrationBuilder.RenameTable(
                name: "StorageFlow",
                newName: "StorageFlows");

            migrationBuilder.RenameIndex(
                name: "IX_StorageFlow_MasterTableId",
                table: "StorageFlows",
                newName: "IX_StorageFlows_MasterTableId");

            migrationBuilder.AlterColumn<int>(
                name: "StorageInterval",
                table: "StorageFlows",
                type: "int",
                nullable: false,
                defaultValue: 1000,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "StorageFlows",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StorageFlows",
                table: "StorageFlows",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "StorageFlowDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    StorageFlowId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DeviceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StorageFlowMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    StorageFlowId = table.Column<Guid>(type: "char(36)", nullable: false),
                    MasterTableFieldId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SourcePath = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    TagId = table.Column<Guid>(type: "char(36)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                    table.ForeignKey(
                        name: "FK_StorageFlowMappings_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
                name: "IX_StorageFlowMappings_TagId",
                table: "StorageFlowMappings",
                column: "TagId");

            migrationBuilder.AddForeignKey(
                name: "FK_StorageFlows_MasterTables_MasterTableId",
                table: "StorageFlows",
                column: "MasterTableId",
                principalTable: "MasterTables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StorageFlows_MasterTables_MasterTableId",
                table: "StorageFlows");

            migrationBuilder.DropTable(
                name: "StorageFlowDevices");

            migrationBuilder.DropTable(
                name: "StorageFlowMappings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StorageFlows",
                table: "StorageFlows");

            migrationBuilder.RenameTable(
                name: "StorageFlows",
                newName: "StorageFlow");

            migrationBuilder.RenameIndex(
                name: "IX_StorageFlows_MasterTableId",
                table: "StorageFlow",
                newName: "IX_StorageFlow_MasterTableId");

            migrationBuilder.AlterColumn<int>(
                name: "StorageInterval",
                table: "StorageFlow",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1000);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "StorageFlow",
                type: "tinyint(1)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_StorageFlow",
                table: "StorageFlow",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StorageFlow_MasterTables_MasterTableId",
                table: "StorageFlow",
                column: "MasterTableId",
                principalTable: "MasterTables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
