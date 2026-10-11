using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThesisDemo.Infrastructure.Persistence.Sqlite
{
    /// <inheritdoc />
    public partial class AddSegmentAndArtifactEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    ProvenanceJson = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndexMembers",
                columns: table => new
                {
                    IndexId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SegmentSetId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexMembers", x => new { x.IndexId, x.AssetId });
                });

            migrationBuilder.CreateTable(
                name: "SegmentSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfigHash = table.Column<string>(type: "TEXT", nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SegmentSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LegacySegmentId = table.Column<string>(type: "TEXT", nullable: false),
                    StartSec = table.Column<double>(type: "REAL", nullable: false),
                    EndSec = table.Column<double>(type: "REAL", nullable: false),
                    Caption = table.Column<string>(type: "TEXT", nullable: false),
                    TranscriptText = table.Column<string>(type: "TEXT", nullable: true),
                    OcrText = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailArtifactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoSegments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_RelativePath",
                table: "Artifacts",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoSegments_SegmentSetId_LegacySegmentId",
                table: "VideoSegments",
                columns: new[] { "SegmentSetId", "LegacySegmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoSegments_SegmentSetId_StartSec",
                table: "VideoSegments",
                columns: new[] { "SegmentSetId", "StartSec" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Artifacts");

            migrationBuilder.DropTable(
                name: "IndexMembers");

            migrationBuilder.DropTable(
                name: "SegmentSets");

            migrationBuilder.DropTable(
                name: "VideoSegments");
        }
    }
}
