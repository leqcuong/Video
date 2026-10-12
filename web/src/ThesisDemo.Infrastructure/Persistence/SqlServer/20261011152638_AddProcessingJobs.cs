using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThesisDemo.Infrastructure.Persistence.SqlServer
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
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CollectionId",
                table: "Jobs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Jobs",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InputHash",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentJobId",
                table: "Jobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgressJson",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestJson",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "RunId",
                table: "Jobs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "JobEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobEvents", x => x.Id);
                });

            migrationBuilder.Sql("UPDATE Jobs SET IdempotencyKey = N'legacy-' + CONVERT(nvarchar(36), Id) WHERE IdempotencyKey = N''");

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
