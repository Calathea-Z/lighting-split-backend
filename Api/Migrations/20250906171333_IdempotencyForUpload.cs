using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyForUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Receipts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_BlobContainer_BlobName",
                table: "Receipts",
                columns: new[] { "BlobContainer", "BlobName" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_OwnerUserId_IdempotencyKey",
                table: "Receipts",
                columns: new[] { "OwnerUserId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Receipts_BlobContainer_BlobName",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_OwnerUserId_IdempotencyKey",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Receipts");
        }
    }
}
