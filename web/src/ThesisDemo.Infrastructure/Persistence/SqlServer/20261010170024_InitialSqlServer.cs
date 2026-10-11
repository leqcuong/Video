using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThesisDemo.Infrastructure.Persistence.SqlServer
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ActiveIndexId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndexSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmbeddingModel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Dimension = table.Column<int>(type: "int", nullable: false),
                    RowCount = table.Column<long>(type: "bigint", nullable: false),
                    ConfigHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClaimedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingAssetAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    OutputRoot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingAssetAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConfigHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputSnapshotHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VideoAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegacyAssetId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MediaArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sha256 = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DurationSec = table.Column<double>(type: "float", nullable: false),
                    Fps = table.Column<double>(type: "float", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAssets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndexSnapshots_CollectionId_State",
                table: "IndexSnapshots",
                columns: new[] { "CollectionId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_State_CreatedUtc",
                table: "Jobs",
                columns: new[] { "State", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingAssetAttempts_RunId_AssetId_AttemptToken",
                table: "ProcessingAssetAttempts",
                columns: new[] { "RunId", "AssetId", "AttemptToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRuns_State_CreatedUtc",
                table: "ProcessingRuns",
                columns: new[] { "State", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VideoAssets_CollectionId_LegacyAssetId",
                table: "VideoAssets",
                columns: new[] { "CollectionId", "LegacyAssetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "IndexSnapshots");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "ProcessingAssetAttempts");

            migrationBuilder.DropTable(
                name: "ProcessingRuns");

            migrationBuilder.DropTable(
                name: "VideoAssets");
        }
    }
}
