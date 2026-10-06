using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPilot.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MailInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailChecks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WindowSinceUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ThreadsRead = table.Column<int>(type: "INTEGER", nullable: false),
                    Waiting = table.Column<int>(type: "INTEGER", nullable: false),
                    AlertThreads = table.Column<int>(type: "INTEGER", nullable: false),
                    Filtered = table.Column<int>(type: "INTEGER", nullable: false),
                    Listings = table.Column<int>(type: "INTEGER", nullable: false),
                    OffersAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    FilteredChips = table.Column<string>(type: "TEXT", nullable: false),
                    RoleFilters = table.Column<string>(type: "TEXT", nullable: false),
                    Warnings = table.Column<string>(type: "TEXT", nullable: false),
                    Roles = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailWaiting",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LastMessageId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LastCheckId = table.Column<long>(type: "INTEGER", nullable: false),
                    RuleId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Pill = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Tone = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SenderName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SenderAddress = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Timeline = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NextAction = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    LastReceivedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RailUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RailNote = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DoneAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailWaiting", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailChecks_StartedAtUtc",
                table: "MailChecks",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MailWaiting_LastCheckId",
                table: "MailWaiting",
                column: "LastCheckId");

            migrationBuilder.CreateIndex(
                name: "IX_MailWaiting_ThreadId",
                table: "MailWaiting",
                column: "ThreadId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailChecks");

            migrationBuilder.DropTable(
                name: "MailWaiting");
        }
    }
}
