using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptReviewMeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "Receipts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ReconcileDelta",
                table: "Receipts",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_NeedsReview",
                table: "Receipts",
                column: "NeedsReview");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Receipts_NeedsReview",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "ReconcileDelta",
                table: "Receipts");
        }
    }
}
