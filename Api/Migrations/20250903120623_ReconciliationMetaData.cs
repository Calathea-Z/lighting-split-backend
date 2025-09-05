using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliationMetaData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReconcileDelta",
                table: "Receipts",
                newName: "Discrepancy");

            migrationBuilder.AddColumn<decimal>(
                name: "BaselineSubtotal",
                table: "Receipts",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ComputedItemsSubtotal",
                table: "Receipts",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "Receipts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemGenerated",
                table: "ReceiptItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaselineSubtotal",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "ComputedItemsSubtotal",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "IsSystemGenerated",
                table: "ReceiptItems");

            migrationBuilder.RenameColumn(
                name: "Discrepancy",
                table: "Receipts",
                newName: "ReconcileDelta");
        }
    }
}
