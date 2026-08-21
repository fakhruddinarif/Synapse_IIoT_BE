using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTagHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TagHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TagId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DeviceId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SourceTs = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    GatewayTs = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    NumericValue = table.Column<double>(type: "double", nullable: true),
                    BoolValue = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    TextValue = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    RawValue = table.Column<double>(type: "double", nullable: true),
                    Quality = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Note = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagHistories_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TagHistories_DeviceId_SourceTs",
                table: "TagHistories",
                columns: new[] { "DeviceId", "SourceTs" });

            migrationBuilder.CreateIndex(
                name: "IX_TagHistories_TagId_SourceTs",
                table: "TagHistories",
                columns: new[] { "TagId", "SourceTs" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TagHistories");
        }
    }
}
