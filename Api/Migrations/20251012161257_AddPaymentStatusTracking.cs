using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentStatusTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SplitParticipantResults_SplitResultId",
                table: "SplitParticipantResults");

            migrationBuilder.AddColumn<string>(
                name: "PaymentNote",
                table: "SplitParticipantResults",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentSource",
                table: "SplitParticipantResults",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "SplitParticipantResults",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaymentUpdatedAt",
                table: "SplitParticipantResults",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedByUserId",
                table: "SplitParticipantResults",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentStatusEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SplitResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    SplitParticipantResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NewStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentStatusEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentStatusEvents_SplitParticipantResults_SplitParticipan~",
                        column: x => x.SplitParticipantResultId,
                        principalTable: "SplitParticipantResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentStatusEvents_SplitResults_SplitResultId",
                        column: x => x.SplitResultId,
                        principalTable: "SplitResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SplitParticipantResults_PaymentUpdatedAt",
                table: "SplitParticipantResults",
                column: "PaymentUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SplitParticipantResults_SplitResultId_PaymentStatus",
                table: "SplitParticipantResults",
                columns: new[] { "SplitResultId", "PaymentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusEvents_ActorId",
                table: "PaymentStatusEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusEvents_CorrelationId",
                table: "PaymentStatusEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusEvents_SplitParticipantResultId_Timestamp",
                table: "PaymentStatusEvents",
                columns: new[] { "SplitParticipantResultId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusEvents_SplitResultId_Timestamp",
                table: "PaymentStatusEvents",
                columns: new[] { "SplitResultId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentStatusEvents");

            migrationBuilder.DropIndex(
                name: "IX_SplitParticipantResults_PaymentUpdatedAt",
                table: "SplitParticipantResults");

            migrationBuilder.DropIndex(
                name: "IX_SplitParticipantResults_SplitResultId_PaymentStatus",
                table: "SplitParticipantResults");

            migrationBuilder.DropColumn(
                name: "PaymentNote",
                table: "SplitParticipantResults");

            migrationBuilder.DropColumn(
                name: "PaymentSource",
                table: "SplitParticipantResults");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "SplitParticipantResults");

            migrationBuilder.DropColumn(
                name: "PaymentUpdatedAt",
                table: "SplitParticipantResults");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "SplitParticipantResults");

            migrationBuilder.CreateIndex(
                name: "IX_SplitParticipantResults_SplitResultId",
                table: "SplitParticipantResults",
                column: "SplitResultId");
        }
    }
}
