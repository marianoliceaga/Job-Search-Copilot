using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPilot.Mobile.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Offers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FollowedCompanies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Ats = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BoardToken = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowedCompanies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceJobId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Company = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ApplyUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    EmploymentType = table.Column<string>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SalaryMin = table.Column<decimal>(type: "TEXT", nullable: true),
                    SalaryMax = table.Column<decimal>(type: "TEXT", nullable: true),
                    SalaryCurrency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    SalaryPeriod = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    SalaryText = table.Column<string>(type: "TEXT", nullable: true),
                    UrlKey = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 800, nullable: false),
                    Sightings = table.Column<string>(type: "TEXT", nullable: false),
                    IsDiscarded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPostings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Fetched = table.Column<int>(type: "INTEGER", nullable: false),
                    Matched = table.Column<int>(type: "INTEGER", nullable: false),
                    Added = table.Column<int>(type: "INTEGER", nullable: false),
                    Updated = table.Column<int>(type: "INTEGER", nullable: false),
                    Merged = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobAnalyses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobPostingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FitScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobAnalyses_JobPostings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalTable: "JobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowedCompanies_Ats_BoardToken",
                table: "FollowedCompanies",
                columns: new[] { "Ats", "BoardToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobAnalyses_JobPostingId_CreatedAtUtc",
                table: "JobAnalyses",
                columns: new[] { "JobPostingId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_Fingerprint",
                table: "JobPostings",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_FirstSeenAtUtc",
                table: "JobPostings",
                column: "FirstSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_JobPostings_UrlKey",
                table: "JobPostings",
                column: "UrlKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceRuns_SourceKey_StartedAtUtc",
                table: "SourceRuns",
                columns: new[] { "SourceKey", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowedCompanies");

            migrationBuilder.DropTable(
                name: "JobAnalyses");

            migrationBuilder.DropTable(
                name: "SourceRuns");

            migrationBuilder.DropTable(
                name: "JobPostings");
        }
    }
}
