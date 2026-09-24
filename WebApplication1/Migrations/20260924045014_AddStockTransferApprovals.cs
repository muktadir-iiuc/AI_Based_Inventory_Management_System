using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddStockTransferApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                table: "StockTransfers",
                type: "int",
                nullable: false,
                // Transfers that predate approvals were posted directly: backfill them as Approved (2).
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "StockTransfers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "StockTransfers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "StockTransfers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "StockTransfers",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "StockTransfers");
        }
    }
}
