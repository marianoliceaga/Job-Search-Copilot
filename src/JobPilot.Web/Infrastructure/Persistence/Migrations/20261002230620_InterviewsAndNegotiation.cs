using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPilot.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InterviewsAndNegotiation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompensationOffers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Company = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    JobPostingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BaseAmount = table.Column<double>(type: "REAL", nullable: false),
                    Period = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    HoursPerMonth = table.Column<double>(type: "REAL", nullable: false),
                    AnnualBonus = table.Column<double>(type: "REAL", nullable: false),
                    AnnualBenefits = table.Column<double>(type: "REAL", nullable: false),
                    AnnualEquity = table.Column<double>(type: "REAL", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PaidTimeOffDays = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompensationOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompensationOffers_JobPostings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalTable: "JobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "InterviewQuestionSets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobPostingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewQuestionSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterviewQuestionSets_JobPostings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalTable: "JobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StarStories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobPostingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MasterCvVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StarStories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StarStories_JobPostings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalTable: "JobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PracticeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    JobPostingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OfferId = table.Column<long>(type: "INTEGER", nullable: true),
                    Focus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PlannedQuestions = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxFollowUps = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SystemInstruction = table.Column<string>(type: "TEXT", nullable: false),
                    OpeningMessage = table.Column<string>(type: "TEXT", nullable: false),
                    Turns = table.Column<string>(type: "TEXT", nullable: false),
                    Feedback = table.Column<string>(type: "TEXT", nullable: true),
                    OverallScore = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeSessions_CompensationOffers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "CompensationOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PracticeSessions_JobPostings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalTable: "JobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompensationOffers_JobPostingId",
                table: "CompensationOffers",
                column: "JobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewQuestionSets_JobPostingId_CreatedAtUtc",
                table: "InterviewQuestionSets",
                columns: new[] { "JobPostingId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_JobPostingId",
                table: "PracticeSessions",
                column: "JobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_OfferId",
                table: "PracticeSessions",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_StartedAtUtc",
                table: "PracticeSessions",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StarStories_JobPostingId",
                table: "StarStories",
                column: "JobPostingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterviewQuestionSets");

            migrationBuilder.DropTable(
                name: "PracticeSessions");

            migrationBuilder.DropTable(
                name: "StarStories");

            migrationBuilder.DropTable(
                name: "CompensationOffers");
        }
    }
}
