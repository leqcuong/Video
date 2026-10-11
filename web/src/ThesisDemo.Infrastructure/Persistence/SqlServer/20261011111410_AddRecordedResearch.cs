using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThesisDemo.Infrastructure.Persistence.SqlServer
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProvenanceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordedEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecordedQueries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Query = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaselineRank = table.Column<int>(type: "int", nullable: true),
                    RerankedRank = table.Column<int>(type: "int", nullable: true),
                    BaselineTop1Iou = table.Column<double>(type: "float", nullable: false),
                    RerankedTop1Iou = table.Column<double>(type: "float", nullable: false),
                    ExpectedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetrievedJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
