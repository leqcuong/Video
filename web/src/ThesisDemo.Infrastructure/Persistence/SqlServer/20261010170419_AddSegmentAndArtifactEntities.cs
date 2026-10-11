using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThesisDemo.Infrastructure.Persistence.SqlServer
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ByteLength = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProvenanceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndexMembers",
                columns: table => new
                {
                    IndexId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SegmentSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexMembers", x => new { x.IndexId, x.AssetId });
                });

            migrationBuilder.CreateTable(
                name: "SegmentSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SegmentSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacySegmentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StartSec = table.Column<double>(type: "float", nullable: false),
                    EndSec = table.Column<double>(type: "float", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TranscriptText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OcrText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ThumbnailArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
