using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class Update_Indexes_Owner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Owners_KeyHash",
                table: "Owners");

            migrationBuilder.AlterColumn<string>(
                name: "KeyHash",
                table: "Owners",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "IX_Owners_CreatedAt",
                table: "Owners",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Owners_LastSeenAt",
                table: "Owners",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "ux_owners_keyhash_active",
                table: "Owners",
                column: "KeyHash",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Owners_CreatedAt",
                table: "Owners");

            migrationBuilder.DropIndex(
                name: "IX_Owners_LastSeenAt",
                table: "Owners");

            migrationBuilder.DropIndex(
                name: "ux_owners_keyhash_active",
                table: "Owners");

            migrationBuilder.AlterColumn<string>(
                name: "KeyHash",
                table: "Owners",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.CreateIndex(
                name: "IX_Owners_KeyHash",
                table: "Owners",
                column: "KeyHash",
                unique: true);
        }
    }
}
