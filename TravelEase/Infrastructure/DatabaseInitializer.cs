using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelEase.Configuration;
using TravelEase.Data;

namespace TravelEase.Infrastructure;

/// <summary>Shared, thread-safe view of the database start-up state (read by the readiness check and Settings page).</summary>
public sealed class DatabaseState
{
    private readonly object _lock = new();

    public bool IsReady { get; private set; }
    public int Attempts { get; private set; }
    public string Message { get; private set; } = "Waiting for database initialisation";
    public DateTime? ReadyAtUtc { get; private set; }

    public void Attempt(int attempt)
    {
        lock (_lock) { Attempts = attempt; Message = $"Initialising database (attempt {attempt})"; }
    }

    public void Ready(string message)
    {
        lock (_lock) { IsReady = true; Message = message; ReadyAtUtc = DateTime.UtcNow; }
    }

    public void Failed(string message)
    {
        lock (_lock) { IsReady = false; Message = message; }
    }
}

/// <summary>
/// Applies EF Core migrations and seeds sample data in the background, retrying until PostgreSQL
/// is reachable. Because this runs in the background, the web server starts immediately:
///   /health/live  -> 200 as soon as the process is up
///   /health/ready -> 503 until the database is migrated and reachable
/// This mirrors how containers behave when the database container starts later than the app.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    DatabaseState state,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.ApplyMigrationsOnStartup && !settings.SeedSampleData)
        {
            state.Ready("Start-up migration and seeding disabled by configuration");
            logger.LogInformation("Database start-up initialisation disabled (Database:ApplyMigrationsOnStartup=false, Database:SeedSampleData=false)");
            return;
        }

        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            state.Attempt(attempt);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TravelEaseDbContext>();

                if (settings.ApplyMigrationsOnStartup)
                {
                    var pending = (await db.Database.GetPendingMigrationsAsync(stoppingToken)).ToList();
                    if (pending.Count > 0)
                    {
                        logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                    }
                    await db.Database.MigrateAsync(stoppingToken);
                    logger.LogInformation("Database schema is up to date");
                }

                if (settings.SeedSampleData)
                {
                    var seeder = scope.ServiceProvider.GetRequiredService<SampleDataSeeder>();
                    await seeder.SeedAsync(stoppingToken);
                }

                state.Ready($"Database initialised after {attempt} attempt(s)");
                logger.LogInformation("Database initialisation completed after {Attempts} attempt(s); application is ready", attempt);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(settings.RetryDelaySeconds * Math.Min(attempt, 6), 60));
                state.Failed($"Database unavailable: {ex.GetBaseException().Message}");

                if (settings.MaxStartupRetries > 0 && attempt >= settings.MaxStartupRetries)
                {
                    logger.LogCritical(ex,
                        "Database connection failed after {Attempts} attempt(s). Giving up; readiness will report Unhealthy. Check DB_HOST / ConnectionStrings__DefaultConnection",
                        attempt);
                    return;
                }

                logger.LogError(
                    "Database connection failed (attempt {Attempt}): {Error}. Retrying in {DelaySeconds}s",
                    attempt, ex.GetBaseException().Message, delay.TotalSeconds);

                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
