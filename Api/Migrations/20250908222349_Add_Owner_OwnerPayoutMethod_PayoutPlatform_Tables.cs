using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class Add_Owner_OwnerPayoutMethod_PayoutPlatform_Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Owners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Owners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayoutPlatforms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LinkTemplate = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SupportsAmount = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsNote = table.Column<bool>(type: "boolean", nullable: false),
                    HandlePattern = table.Column<string>(type: "text", nullable: true),
                    PrefixToStrip = table.Column<string>(type: "text", nullable: true),
                    IsInstructionsOnly = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutPlatforms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OwnerPayoutMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformId = table.Column<int>(type: "integer", nullable: false),
                    HandleOrUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QrImageBlobPath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerPayoutMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerPayoutMethods_Owners_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Owners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OwnerPayoutMethods_PayoutPlatforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "PayoutPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PayoutPlatforms",
                columns: new[] { "Id", "DisplayName", "HandlePattern", "IsInstructionsOnly", "Key", "LinkTemplate", "PrefixToStrip", "SortOrder", "SupportsAmount", "SupportsNote" },
                values: new object[,]
                {
                    { 1, "Venmo", "^[A-Za-z0-9_.]+$", false, "venmo", "https://account.venmo.com/pay?txn=pay&recipients={handle}&amount={amount}&note={note}", "@", 10, true, true },
                    { 2, "Cash App", "^[A-Za-z0-9_]+$", false, "cashapp", "https://cash.app/${handle}?amount={amount}&note={note}", "$", 20, true, true },
                    { 3, "PayPal.Me", "^[A-Za-z0-9.]+$", false, "paypalme", "https://paypal.me/{handle}/{amount}", "paypal.me/", 30, true, false },
                    { 4, "Zelle", "(^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$)|(^\\+?1?\\d{10}$)", true, "zelle", null, null, 40, false, false },
                    { 5, "Apple Cash", ".{1,256}", true, "applecash", null, null, 50, false, false },
                    { 6, "Custom URL", "^https://", false, "custom", "{handle}", null, 60, false, false }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPayoutMethods_OwnerId_IsDefault",
                table: "OwnerPayoutMethods",
                columns: new[] { "OwnerId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPayoutMethods_PlatformId",
                table: "OwnerPayoutMethods",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_Owners_KeyHash",
                table: "Owners",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayoutPlatforms_Key",
                table: "PayoutPlatforms",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerPayoutMethods");

            migrationBuilder.DropTable(
                name: "Owners");

            migrationBuilder.DropTable(
                name: "PayoutPlatforms");
        }
    }
}
