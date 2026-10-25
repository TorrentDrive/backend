using Hangfire;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using TorrentDrive.API.Extensions;
using TorrentDrive.API.Filters;
using TorrentDrive.Application.Extensions;
using TorrentDrive.Infrastructure.Extensions;
using TorrentDrive.Infrastructure.Services;

const string ServiceName = "torrentdrive-api";

// Emits the configuration reference and exits, before any service is touched.
//
// The reference used to be written by hand in five places and no two agreed on
// which variables existed, which were required, or what the defaults were. It is
// now derived from the annotated option types, so CI can publish it and the docs
// site can render something the code actually implements.
//
//   dotnet run --project TorrentDrive.API -- --dump-config-schema
if (args.Contains("--dump-config-schema"))
{
    Console.WriteLine(TorrentDrive.Core.Configuration.ConfigSchema.ToJson());
    return;
}

// Bootstrap logger for startup errors only
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog (replaces bootstrap logger)
    builder.Configuration.ConfigureSharedSerilog(ServiceName, builder.Environment.EnvironmentName);
    builder.Host.UseSerilog();

    Log.Information("Starting {ServiceName}", ServiceName);

    // Infrastructure
    builder.Services.AddSharedDatabase(builder.Configuration);
    builder.Services.AddSharedRedis(builder.Configuration);
    builder.Services.AddTorrentDriveOpenTelemetry(ServiceName, builder.Configuration, builder.Environment, true);
    builder.Services.AddSharedHangfireBase(builder.Configuration);

    // Application Services
    builder.Services.AddApplicationServices();
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);
    builder.Services.AddIdentityServices(builder.Configuration);
    builder.Services.AddHttpClient();
    builder.Services.AddHealthChecks();



    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    var app = builder.Build();

    // Conditionally apply database migrations at startup (gated by config flag)
    var applyMigrations = app.Configuration.GetValue<bool>("APPLY_MIGRATIONS");

    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();

        // Rollback safety.
        //
        // Users are told they can pin an older image and roll back. That is only
        // true if an older build refuses to run against a schema written by a
        // newer one. Without this check the app starts happily against a
        // database it does not understand and fails later, during real work, in
        // ways that look like data corruption rather than a bad rollback.
        //
        // Best effort: if the database cannot be read at all we say so and carry
        // on, because the migration step below will fail with a better message.
        try
        {
            var schemaContext = services.GetRequiredService<TorrentDrive.Infrastructure.Data.ApplicationDbContext>();
            var appliedMigrations = await schemaContext.Database.GetAppliedMigrationsAsync();
            var knownMigrations = schemaContext.Database.GetMigrations();
            var unknownMigrations = appliedMigrations.Except(knownMigrations).ToList();

            if (unknownMigrations.Count > 0)
            {
                var allowSchemaAhead = app.Configuration.GetValue<bool>("ALLOW_SCHEMA_AHEAD");

                logger.LogError(
                    "The database has {Count} migration(s) this build does not know about: {Migrations}. " +
                    "This usually means you rolled back to an older image past a schema change. " +
                    "Restore the database backup taken before the upgrade, or set ALLOW_SCHEMA_AHEAD=true " +
                    "to start anyway and accept the risk.",
                    unknownMigrations.Count,
                    string.Join(", ", unknownMigrations));

                if (!allowSchemaAhead)
                {
                    throw new InvalidOperationException(
                        $"Database schema is ahead of this build ({string.Join(", ", unknownMigrations)}). " +
                        "Refusing to start. Set ALLOW_SCHEMA_AHEAD=true to override.");
                }

                logger.LogWarning(
                    "ALLOW_SCHEMA_AHEAD is set. Starting against a newer schema anyway.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not compare the database schema against this build");
        }

        if (!applyMigrations)
        {
            logger.LogInformation("Database migrations skipped (APPLY_MIGRATIONS=false)");
        }
        else
        {
            try
            {
                var context = services.GetRequiredService<TorrentDrive.Infrastructure.Data.ApplicationDbContext>();
                logger.LogInformation("Acquiring advisory lock for database migration...");

                // Use PostgreSQL advisory lock to prevent concurrent migration attempts
                const int advisoryLockId = 839_275_194;

                using var connection = context.Database.GetDbConnection();
                await connection.OpenAsync();

                using var lockCommand = connection.CreateCommand();
                lockCommand.CommandText = $"SELECT pg_advisory_lock({advisoryLockId})";
                await lockCommand.ExecuteNonQueryAsync();

                try
                {
                    logger.LogInformation("Advisory lock acquired. Checking for pending database migrations...");
                    await context.Database.MigrateAsync();
                    logger.LogInformation("Database migrations applied successfully");
                }
                finally
                {
                    using var unlockCommand = connection.CreateCommand();
                    unlockCommand.CommandText = $"SELECT pg_advisory_unlock({advisoryLockId})";
                    await unlockCommand.ExecuteNonQueryAsync();
                    logger.LogInformation("Advisory lock released");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while migrating the database");
                throw;
            }
        }
    }

    // Middleware
    app.UseExceptionHandler();
    app.UseCors("AllowAll");

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }
    else if (app.Configuration.GetValue<bool>("Api:UseHttpsRedirection"))
    {
        // Off by default: in every deployment of this stack the container listens on
        // plain HTTP:8080 and TLS is terminated upstream. Enabling it without an HTTPS
        // port makes ASP.NET log "Failed to determine the https port for redirect" on
        // every request, including each Prometheus scrape.
        app.UseHttpsRedirection();
        app.UseHsts();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireAuthorizationFilter()],
        DashboardTitle = "TorrentDrive Jobs"
    });

    // Only when the exporter was actually registered. Mapping the scraping endpoint
    // without it throws at startup, which turned "Prometheus off" — now a setting a user
    // can toggle — into a container that will not boot.
    if (builder.Configuration.GetSection("Observability").GetValue("EnablePrometheus", true))
    {
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
    }
    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapControllers();

    Log.Information("{ServiceName} started successfully", ServiceName);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
