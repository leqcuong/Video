using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThesisDemo.Infrastructure.Persistence.Sqlite
{
    /// <inheritdoc />
    public partial class AddProcessingJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelRequested",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CollectionId",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InputHash",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentJobId",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgressJson",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestJson",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "RunId",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "JobEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    DetailJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobEvents", x => x.Id);
                });

            // Existing foundation jobs have no idempotency key. Give each a stable unique key
            // before introducing the collection/key unique index.
            migrationBuilder.Sql("UPDATE Jobs SET IdempotencyKey = 'legacy-' || Id WHERE IdempotencyKey = ''");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CollectionId_IdempotencyKey",
                table: "Jobs",
                columns: new[] { "CollectionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobEvents_JobId_Id",
                table: "JobEvents",
                columns: new[] { "JobId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobEvents");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_CollectionId_IdempotencyKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CancelRequested",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CollectionId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "InputHash",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ParentJobId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ProgressJson",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "RequestJson",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "RunId",
                table: "Jobs");
        }
    }
}
