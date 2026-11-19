using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TravelEase.Configuration;
using TravelEase.Data;
using TravelEase.Infrastructure;
using TravelEase.Services;

namespace TravelEase.HealthChecks;

public static class HealthTags
{
    public const string Live = "live";
    public const string Ready = "ready";
}

/// <summary>Opens a real connection to PostgreSQL and runs a trivial query.</summary>
public sealed class DatabaseHealthCheck(TravelEaseDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var data = new Dictionary<string, object> { ["host"] = connection.DataSource, ["database"] = connection.Database };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            data["serverVersion"] = connection.ServerVersion;
            data["latencyMs"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1);
            return HealthCheckResult.Healthy("PostgreSQL is reachable", data);
        }
        catch (Exception ex)
        {
            logger.LogError("Database health check failed for {Host}/{Database}: {Error}", connection.DataSource, connection.Database, ex.GetBaseException().Message);
            return new HealthCheckResult(context.Registration.FailureStatus, "PostgreSQL is not reachable", ex, data);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

/// <summary>Reports whether migrations + seeding have completed.</summary>
public sealed class DatabaseInitializationHealthCheck(DatabaseState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object> { ["attempts"] = state.Attempts };
        if (state.ReadyAtUtc.HasValue) data["readyAtUtc"] = state.ReadyAtUtc.Value;

        return Task.FromResult(state.IsReady
            ? HealthCheckResult.Healthy(state.Message, data)
            : new HealthCheckResult(context.Registration.FailureStatus, state.Message, data: data));
    }
}

/// <summary>Verifies the reports directory (Docker volume) exists and is writable by the current user.</summary>
public sealed class StorageHealthCheck(IReportFileService files) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var info = files.GetStorageInfo();
        var data = new Dictionary<string, object> { ["path"] = info.Path, ["files"] = info.Files.Count };

        return Task.FromResult(info.Writable
            ? HealthCheckResult.Healthy("Report storage is writable", data)
            : new HealthCheckResult(context.Registration.FailureStatus, $"Report storage is not writable: {info.Path}", data: data));
    }
}

/// <summary>Writes health reports as JSON (default ASP.NET Core output is plain text).</summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var app = context.RequestServices.GetRequiredService<IOptions<AppOptions>>().Value;
        var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();

        var payload = new
        {
            status = report.Status.ToString(),
            application = app.Name,
            version = AppVersion.Resolve(app),
            environment = env.EnvironmentName,
            host = Environment.MachineName,
            timestampUtc = DateTime.UtcNow,
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                tags = e.Value.Tags,
                error = e.Value.Exception?.GetBaseException().Message,
                data = e.Value.Data.Count == 0 ? null : e.Value.Data
            })
        };

        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Json));
    }
}
