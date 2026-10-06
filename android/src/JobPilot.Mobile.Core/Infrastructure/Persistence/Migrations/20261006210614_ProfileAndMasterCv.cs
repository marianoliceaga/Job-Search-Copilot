using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPilot.Mobile.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileAndMasterCv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MasterCvs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterCvs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MasterCvVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterCvVersions", x => x.Id);
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
                name: "IX_MasterCvVersions_Version",
                table: "MasterCvVersions",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MasterCvs");

            migrationBuilder.DropTable(
                name: "MasterCvVersions");

            migrationBuilder.DropTable(
                name: "Profiles");
        }
    }
}
