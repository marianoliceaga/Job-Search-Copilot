using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPilot.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompaniesAndOutreach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Website = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: true),
                    Sources = table.Column<string>(type: "TEXT", nullable: false),
                    Facts = table.Column<string>(type: "TEXT", nullable: false),
                    Gaps = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResearchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JobPostingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Length = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RecipientName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RecipientRole = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    SentText = table.Column<string>(type: "TEXT", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutreachMessages_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OutreachMessages_JobPostings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalTable: "JobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_NameKey",
                table: "Companies",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutreachMessages_CompanyId",
                table: "OutreachMessages",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachMessages_CreatedAtUtc",
                table: "OutreachMessages",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachMessages_JobPostingId",
                table: "OutreachMessages",
                column: "JobPostingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutreachMessages");

            migrationBuilder.DropTable(
                name: "Companies");
        }
    }
}
