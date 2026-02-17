using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPollingIntervalToDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "MasterTables",
                newName: "IsActive");

            migrationBuilder.AddColumn<int>(
                name: "PollingInterval",
                table: "Devices",
                type: "int",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.CreateTable(
                name: "StorageFlow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StorageInterval = table.Column<int>(type: "int", nullable: false),
                    MasterTableId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageFlow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageFlow_MasterTables_MasterTableId",
                        column: x => x.MasterTableId,
                        principalTable: "MasterTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StorageFlow_MasterTableId",
                table: "StorageFlow",
                column: "MasterTableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageFlow");

            migrationBuilder.DropColumn(
                name: "PollingInterval",
                table: "Devices");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "MasterTables",
                newName: "IsEnabled");
        }
    }
}
