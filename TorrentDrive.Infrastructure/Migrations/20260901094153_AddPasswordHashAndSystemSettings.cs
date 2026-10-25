using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TorrentDrive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordHashAndSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                schema: "dev",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                schema: "dev",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SetupCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EnableFailover = table.Column<bool>(type: "boolean", nullable: false),
                    MaxFailoverAttempts = table.Column<int>(type: "integer", nullable: false),
                    FailureThreshold = table.Column<int>(type: "integer", nullable: false),
                    HealthCacheTtlSeconds = table.Column<int>(type: "integer", nullable: false),
                    QuotaHeadroomRatio = table.Column<double>(type: "double precision", nullable: false),
                    DegradedFreeQuotaRatio = table.Column<double>(type: "double precision", nullable: false),
                    ProbeTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    HangfireWorkerCount = table.Column<int>(type: "integer", nullable: false),
                    EnablePrometheus = table.Column<bool>(type: "boolean", nullable: false),
                    EnableTracing = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings",
                schema: "dev");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                schema: "dev",
                table: "Users");
        }
    }
}
