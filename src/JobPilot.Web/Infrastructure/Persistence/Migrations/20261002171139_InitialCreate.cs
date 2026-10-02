using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPilot.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LlmCalls",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UseCase = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PromptId = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorKind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    ThoughtsTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CachedTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    ToolUsePromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyMs = table.Column<long>(type: "INTEGER", nullable: false),
                    EstimatedCostUsd = table.Column<double>(type: "REAL", nullable: true),
                    FinishReason = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ResponseId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmCalls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LlmUseCaseSettings",
                columns: table => new
                {
                    UseCase = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ThinkingLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Temperature = table.Column<double>(type: "REAL", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmUseCaseSettings", x => x.UseCase);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Headline = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    YearsOfExperience = table.Column<int>(type: "INTEGER", nullable: true),
                    Roles = table.Column<string>(type: "TEXT", nullable: false),
                    Stack = table.Column<string>(type: "TEXT", nullable: false),
                    Domains = table.Column<string>(type: "TEXT", nullable: false),
                    WorkFromCountry = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    WorkModality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Priorities = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ApplicationLanguage = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ApplicationEmphases = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmCalls_StartedAtUtc",
                table: "LlmCalls",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LlmCalls");

            migrationBuilder.DropTable(
                name: "LlmUseCaseSettings");

            migrationBuilder.DropTable(
                name: "Profiles");
        }
    }
}
