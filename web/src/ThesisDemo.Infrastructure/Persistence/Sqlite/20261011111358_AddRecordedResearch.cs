using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThesisDemo.Infrastructure.Persistence.Sqlite
{
    /// <inheritdoc />
    public partial class AddRecordedResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecordedEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", nullable: false),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProvenanceJson = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordedEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecordedQueries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Query = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    BaselineRank = table.Column<int>(type: "INTEGER", nullable: true),
                    RerankedRank = table.Column<int>(type: "INTEGER", nullable: true),
                    BaselineTop1Iou = table.Column<double>(type: "REAL", nullable: false),
                    RerankedTop1Iou = table.Column<double>(type: "REAL", nullable: false),
                    ExpectedJson = table.Column<string>(type: "TEXT", nullable: false),
                    RetrievedJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordedQueries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordedEvaluations_SourceId",
                table: "RecordedEvaluations",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordedQueries_EvaluationId_Ordinal",
                table: "RecordedQueries",
                columns: new[] { "EvaluationId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordedEvaluations");

            migrationBuilder.DropTable(
                name: "RecordedQueries");
        }
    }
}
