using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using TravelEase.Configuration;
using TravelEase.Data;
using TravelEase.Infrastructure;
using TravelEase.Models.ViewModels;

namespace TravelEase.Services;

public interface ISystemInfoService
{
    Task<SettingsViewModel> GetAsync(CancellationToken ct = default);
}

public sealed class SystemInfoService(
    TravelEaseDbContext db,
    HealthCheckService healthChecks,
    IReportFileService files,
    DatabaseState databaseState,
    IOptions<AppOptions> appOptions,
    IWebHostEnvironment environment,
    IServer server,
    IConfiguration configuration) : ISystemInfoService
{
    private static readonly string[] InterestingPrefixes =
    {
        "ASPNETCORE_", "DOTNET_", "App__", "Database__", "Storage__", "ConnectionStrings__", "DB_", "Logging__", "HEALTHCHECK_", "TZ", "HOSTNAME"
    };

    private static readonly string[] SecretMarkers = { "PASSWORD", "SECRET", "TOKEN", "CONNECTIONSTRING", "KEY" };

    public async Task<SettingsViewModel> GetAsync(CancellationToken ct = default)
    {
        var process = Process.GetCurrentProcess();
        var connectionString = db.Database.GetConnectionString() ?? string.Empty;
        var csb = new NpgsqlConnectionStringBuilder(connectionString);

        bool connected = false;
        string? serverVersion = null, dbError = null;
        double? latency = null;
        IReadOnlyList<string> applied = Array.Empty<string>(), pending = Array.Empty<string>();

        var sw = Stopwatch.StartNew();
        try
        {
            connected = await db.Database.CanConnectAsync(ct);
            latency = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            if (connected)
            {
                serverVersion = await db.Database.SqlQueryRaw<string>("SELECT version() AS \"Value\"").FirstAsync(ct);
                applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
                pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            }
            else
            {
                dbError = "Unable to open a connection.";
            }
        }
        catch (Exception ex)
        {
            dbError = ex.GetBaseException().Message;
        }

        var report = await healthChecks.CheckHealthAsync(ct);

        return new SettingsViewModel
        {
            AppName = appOptions.Value.Name,
            Version = AppVersion.Resolve(appOptions.Value),
            Environment = environment.EnvironmentName,
            HostName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            Framework = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessUser = Environment.UserName,
            ProcessId = Environment.ProcessId,
            RunningInContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase),
            StartedAtUtc = process.StartTime.ToUniversalTime(),
            Uptime = DateTime.Now - process.StartTime,
            ContentRoot = environment.ContentRootPath,
            ListeningUrls = server.Features.Get<IServerAddressesFeature>()?.Addresses.ToList() ?? new List<string>(),
            WorkingSetMb = process.WorkingSet64 / 1024 / 1024,

            DatabaseConnected = connected,
            DatabaseHost = $"{csb.Host}:{csb.Port}",
            DatabaseName = csb.Database ?? string.Empty,
            DatabaseServerVersion = serverVersion,
            DatabaseLatencyMs = latency,
            DatabaseError = dbError,
            MaskedConnectionString = DatabaseConnectionResolver.Mask(connectionString),
            AppliedMigrations = applied,
            PendingMigrations = pending,
            DatabaseInitState = databaseState.Message,

            OverallHealth = report.Status.ToString(),
            HealthEntries = report.Entries.Select(e => new HealthEntryView(
                e.Key, e.Value.Status.ToString(), e.Value.Description, Math.Round(e.Value.Duration.TotalMilliseconds, 1), e.Value.Tags.ToList())).ToList(),

            ReportStorage = files.GetStorageInfo(),
            Configuration = BuildConfiguration()
        };
    }

    private IReadOnlyList<ConfigEntry> BuildConfiguration()
    {
        var entries = new List<ConfigEntry>
        {
            new("App:Name", appOptions.Value.Name, false),
            new("App:CurrencySymbol", appOptions.Value.CurrencySymbol, false),
            new("App:Culture", appOptions.Value.Culture, false),
            new("Database:ApplyMigrationsOnStartup", configuration["Database:ApplyMigrationsOnStartup"] ?? "", false),
            new("Database:SeedSampleData", configuration["Database:SeedSampleData"] ?? "", false),
            new("Storage:ReportsPath", configuration["Storage:ReportsPath"] ?? "", false),
            new("Storage:DataProtectionKeysPath", configuration["Storage:DataProtectionKeysPath"] ?? "", false),
            new("Logging:Console:FormatterName", configuration["Logging:Console:FormatterName"] ?? "", false),
        };

        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            var key = variable.Key.ToString() ?? string.Empty;
            if (!InterestingPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

            var value = variable.Value?.ToString() ?? string.Empty;
            var secret = SecretMarkers.Any(m => key.Replace("_", "").Contains(m, StringComparison.OrdinalIgnoreCase));
            if (key.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new($"env: {key}", SafeMask(value), true));
            }
            else
            {
                entries.Add(new($"env: {key}", secret && !key.EndsWith("_FILE", StringComparison.OrdinalIgnoreCase) ? "********" : value, secret));
            }
        }

        return entries.OrderBy(e => e.Key.StartsWith("env:")).ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string SafeMask(string connectionString)
    {
        try { return DatabaseConnectionResolver.Mask(connectionString); }
        catch { return "********"; }
    }
}
