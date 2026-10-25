using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TorrentDrive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettingsAndTransferSpeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DownloadSpeedBytesPerSecond",
                schema: "dev",
                table: "UserJobs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "UploadSpeedBytesPerSecond",
                schema: "dev",
                table: "UserJobs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "UserSettings",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    // Opt-out: a row created outside UserSettingsService still gets the documented default.
                    DeleteAfterUpload = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSettings_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dev",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_UserId",
                schema: "dev",
                table: "UserSettings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSettings",
                schema: "dev");

            migrationBuilder.DropColumn(
                name: "DownloadSpeedBytesPerSecond",
                schema: "dev",
                table: "UserJobs");

            migrationBuilder.DropColumn(
                name: "UploadSpeedBytesPerSecond",
                schema: "dev",
                table: "UserJobs");
        }
    }
}
