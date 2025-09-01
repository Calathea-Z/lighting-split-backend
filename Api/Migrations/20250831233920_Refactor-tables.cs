using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class Refactortables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReceiptItems_ReceiptId",
                table: "ReceiptItems");

            migrationBuilder.AlterColumn<string>(
                name: "RawText",
                table: "Receipts",
                type: "character varying(100000)",
                maxLength: 100000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100000)",
                oldMaxLength: 100000);

            migrationBuilder.AlterColumn<string>(
                name: "ParseError",
                table: "Receipts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlobContainer",
                table: "Receipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BlobName",
                table: "Receipts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Receipts",
                type: "timestamptz",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Receipts",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AlterColumn<decimal>(
                name: "Qty",
                table: "ReceiptItems",
                type: "numeric(9,3)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ReceiptItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ReceiptItems",
                type: "timestamptz",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<decimal>(
                name: "Discount",
                table: "ReceiptItems",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LineSubtotal",
                table: "ReceiptItems",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LineTotal",
                table: "ReceiptItems",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ReceiptItems",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "ReceiptItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Tax",
                table: "ReceiptItems",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "ReceiptItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "ReceiptItems",
                type: "timestamptz",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ReceiptItems",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_Status_CreatedAt",
                table: "Receipts",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptItems_ReceiptId_Position",
                table: "ReceiptItems",
                columns: new[] { "ReceiptId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Receipts_Status_CreatedAt",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptItems_ReceiptId_Position",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "BlobContainer",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "BlobName",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "Discount",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "LineSubtotal",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "LineTotal",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "Tax",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ReceiptItems");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ReceiptItems");

            migrationBuilder.AlterColumn<string>(
                name: "RawText",
                table: "Receipts",
                type: "character varying(100000)",
                maxLength: 100000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100000)",
                oldMaxLength: 100000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ParseError",
                table: "Receipts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Qty",
                table: "ReceiptItems",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,3)");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptItems_ReceiptId",
                table: "ReceiptItems",
                column: "ReceiptId");
        }
    }
}
