using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class Add_Owner_Phase2_AuditAndVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IssuedAt",
                table: "Owners",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastSeenIp",
                table: "Owners",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSeenUserAgent",
                table: "Owners",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "TokenVersion",
                table: "Owners",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.CreateIndex(
                name: "IX_Owners_IssuedAt",
                table: "Owners",
                column: "IssuedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Owners_IssuedAt",
                table: "Owners");

            migrationBuilder.DropColumn(
                name: "IssuedAt",
                table: "Owners");

            migrationBuilder.DropColumn(
                name: "LastSeenIp",
                table: "Owners");

            migrationBuilder.DropColumn(
                name: "LastSeenUserAgent",
                table: "Owners");

            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "Owners");
        }
    }
}
