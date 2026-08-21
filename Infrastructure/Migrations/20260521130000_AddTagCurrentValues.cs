using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    public partial class AddTagCurrentValues : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CurrentEngValue",
                table: "Tags",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CurrentRawValue",
                table: "Tags",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValueUpdatedAt",
                table: "Tags",
                type: "datetime(6)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentEngValue",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "CurrentRawValue",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "ValueUpdatedAt",
                table: "Tags");
        }
    }
}