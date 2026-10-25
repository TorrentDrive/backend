using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorrentDrive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageHealthAndUploadRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                schema: "dev",
                table: "UserStorageProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HealthStatus",
                schema: "dev",
                table: "UserStorageProfiles",
                type: "text",
                nullable: false,
                // Existing profiles have never been probed.
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHealthCheckAt",
                schema: "dev",
                table: "UserStorageProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastHealthError",
                schema: "dev",
                table: "UserStorageProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuotaTotalBytes",
                schema: "dev",
                table: "UserStorageProfiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuotaUsedBytes",
                schema: "dev",
                table: "UserStorageProfiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowStorageFailover",
                schema: "dev",
                table: "UserJobs",
                type: "boolean",
                nullable: false,
                // Failover is opt-out: existing jobs get it, matching the entity default.
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "FailoverAttempts",
                schema: "dev",
                table: "UserJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastRouteReason",
                schema: "dev",
                table: "UserJobs",
                type: "text",
                nullable: false,
                // Existing jobs have never been rerouted.
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "OriginalStorageProfileId",
                schema: "dev",
                table: "UserJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserJobs_OriginalStorageProfileId",
                schema: "dev",
                table: "UserJobs",
                column: "OriginalStorageProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserJobs_UserStorageProfiles_OriginalStorageProfileId",
                schema: "dev",
                table: "UserJobs",
                column: "OriginalStorageProfileId",
                principalSchema: "dev",
                principalTable: "UserStorageProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserJobs_UserStorageProfiles_OriginalStorageProfileId",
                schema: "dev",
                table: "UserJobs");

            migrationBuilder.DropIndex(
                name: "IX_UserJobs_OriginalStorageProfileId",
                schema: "dev",
                table: "UserJobs");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                schema: "dev",
                table: "UserStorageProfiles");

            migrationBuilder.DropColumn(
                name: "HealthStatus",
                schema: "dev",
                table: "UserStorageProfiles");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckAt",
                schema: "dev",
                table: "UserStorageProfiles");

            migrationBuilder.DropColumn(
                name: "LastHealthError",
                schema: "dev",
                table: "UserStorageProfiles");

            migrationBuilder.DropColumn(
                name: "QuotaTotalBytes",
                schema: "dev",
                table: "UserStorageProfiles");

            migrationBuilder.DropColumn(
                name: "QuotaUsedBytes",
                schema: "dev",
                table: "UserStorageProfiles");

            migrationBuilder.DropColumn(
                name: "AllowStorageFailover",
                schema: "dev",
                table: "UserJobs");

            migrationBuilder.DropColumn(
                name: "FailoverAttempts",
                schema: "dev",
                table: "UserJobs");

            migrationBuilder.DropColumn(
                name: "LastRouteReason",
                schema: "dev",
                table: "UserJobs");

            migrationBuilder.DropColumn(
                name: "OriginalStorageProfileId",
                schema: "dev",
                table: "UserJobs");
        }
    }
}
