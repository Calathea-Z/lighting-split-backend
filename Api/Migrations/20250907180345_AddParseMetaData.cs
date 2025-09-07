using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddParseMetaData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LlmAccepted",
                table: "Receipts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LlmAttempted",
                table: "Receipts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LlmModel",
                table: "Receipts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ParsedAt",
                table: "Receipts",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParsedBy",
                table: "Receipts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParserVersion",
                table: "Receipts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "Receipts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ParsedAt",
                table: "Receipts",
                column: "ParsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ParsedBy_ParsedAt",
                table: "Receipts",
                columns: new[] { "ParsedBy", "ParsedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Receipts_ParsedAt",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_ParsedBy_ParsedAt",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "LlmAccepted",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "LlmAttempted",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "LlmModel",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "ParsedAt",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "ParsedBy",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "ParserVersion",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "Receipts");
        }
    }
}
