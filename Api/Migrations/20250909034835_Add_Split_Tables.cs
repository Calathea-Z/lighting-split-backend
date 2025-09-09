using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class Add_Split_Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SplitSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsFinalized = table.Column<bool>(type: "boolean", nullable: false),
                    ShareCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SplitSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SplitSessions_Owners_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Owners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SplitSessions_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SplitSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    QtyShare = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemClaims_SplitSessions_SplitSessionId",
                        column: x => x.SplitSessionId,
                        principalTable: "SplitSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SplitParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SplitSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SplitParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SplitParticipants_SplitSessions_SplitSessionId",
                        column: x => x.SplitSessionId,
                        principalTable: "SplitSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SplitResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SplitSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SplitResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SplitResults_SplitSessions_SplitSessionId",
                        column: x => x.SplitSessionId,
                        principalTable: "SplitSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SplitParticipantResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SplitResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemsSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAlloc = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAlloc = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TipAlloc = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SplitParticipantResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SplitParticipantResults_SplitResults_SplitResultId",
                        column: x => x.SplitResultId,
                        principalTable: "SplitResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemClaims_SplitSessionId_ReceiptItemId_ParticipantId",
                table: "ItemClaims",
                columns: new[] { "SplitSessionId", "ReceiptItemId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SplitParticipantResults_SplitResultId",
                table: "SplitParticipantResults",
                column: "SplitResultId");

            migrationBuilder.CreateIndex(
                name: "IX_SplitParticipants_SplitSessionId_SortOrder",
                table: "SplitParticipants",
                columns: new[] { "SplitSessionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SplitResults_SplitSessionId",
                table: "SplitResults",
                column: "SplitSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SplitSessions_OwnerId_CreatedAt",
                table: "SplitSessions",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SplitSessions_ReceiptId_CreatedAt",
                table: "SplitSessions",
                columns: new[] { "ReceiptId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SplitSessions_ShareCode",
                table: "SplitSessions",
                column: "ShareCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemClaims");

            migrationBuilder.DropTable(
                name: "SplitParticipantResults");

            migrationBuilder.DropTable(
                name: "SplitParticipants");

            migrationBuilder.DropTable(
                name: "SplitResults");

            migrationBuilder.DropTable(
                name: "SplitSessions");
        }
    }
}
