using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Console;
using TravelEase.Configuration;
using TravelEase.Data;
using TravelEase.HealthChecks;
using TravelEase.Infrastructure;
using TravelEase.Services;

// ---------------------------------------------------------------------------------------------
// Probe mode: "dotnet TravelEase.dll healthcheck [/path]" performs an HTTP health check against
// the running app and exits 0/1. Lets Docker HEALTHCHECK work on images without curl or wget.
// ---------------------------------------------------------------------------------------------
if (args.Length > 0 && string.Equals(args[0], "healthcheck", StringComparison.OrdinalIgnoreCase))
{
    return await HealthProbe.RunAsync(args);
}

// Deterministic parsing/formatting of numbers and dates regardless of the container's locale.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// ----- Logging: console only -> stdout, errors -> stderr (visible via `docker logs`) -----
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Error);
builder.Logging.Configure(o => o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

// ----- Options -----
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();

// ----- Database (PostgreSQL via EF Core). Credentials come only from configuration / env vars -----
var connectionString = DatabaseConnectionResolver.Resolve(builder.Configuration);
builder.Services.AddDbContext<TravelEaseDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
        npgsql.CommandTimeout(databaseOptions.CommandTimeoutSeconds);
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory");
    });
    if (builder.Environment.IsDevelopment()) options.EnableSensitiveDataLogging(false).EnableDetailedErrors();
});

// ----- Data Protection keys (antiforgery). Persist to a volume so restarts don't invalidate forms -----
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("TravelEase");
if (!string.IsNullOrWhiteSpace(storageOptions.DataProtectionKeysPath))
{
    var keysPath = Path.IsPathRooted(storageOptions.DataProtectionKeysPath)
        ? storageOptions.DataProtectionKeysPath
        : Path.Combine(builder.Environment.ContentRootPath, storageOptions.DataProtectionKeysPath);
    try
    {
        Directory.CreateDirectory(keysPath);
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"WARN: Data Protection key directory '{keysPath}' is not usable ({ex.Message}); keys will be kept in memory.");
    }
}

// ----- Application services (dependency injection) -----
builder.Services.AddSingleton<DatabaseState>();
builder.Services.AddSingleton<UiFormatter>();
builder.Services.AddSingleton<IReportFileService, ReportFileService>();
builder.Services.AddScoped<SampleDataSeeder>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ITripService, TripService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ISystemInfoService, SystemInfoService>();
builder.Services.AddHostedService<DatabaseInitializer>();

// ----- Health checks -----
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Application process is running"), tags: new[] { HealthTags.Live })
    .AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy, new[] { HealthTags.Ready })
    .AddCheck<DatabaseInitializationHealthCheck>("database-initialisation", HealthStatus.Unhealthy, new[] { HealthTags.Ready })
    .AddCheck<StorageHealthCheck>("report-storage", HealthStatus.Degraded, new[] { HealthTags.Ready });

// ----- MVC + API -----
builder.Services
    .AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddRouting(options => options.LowercaseUrls = true);

// Give in-flight requests time to finish when `docker stop` sends SIGTERM.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(20));

var app = builder.Build();

// ----- Lifecycle logging -----
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TravelEase.Lifecycle");
var appInfo = app.Services.GetRequiredService<UiFormatter>();
logger.LogInformation(
    "Starting {AppName} {Version} | Environment: {Environment} | Host: {Host} | .NET {Runtime} | User: {User} | PID: {Pid}",
    appInfo.Options.Name, appInfo.Version, app.Environment.EnvironmentName, Environment.MachineName,
    Environment.Version, Environment.UserName, Environment.ProcessId);
logger.LogInformation("Database target: {Connection}", DatabaseConnectionResolver.Mask(connectionString));
logger.LogInformation("Reports directory: {ReportsPath}", app.Services.GetRequiredService<IReportFileService>().ReportsDirectory);

app.Lifetime.ApplicationStarted.Register(() =>
    logger.LogInformation("{AppName} started. Listening on: {Urls}", appInfo.Options.Name, string.Join(", ", app.Urls)));
app.Lifetime.ApplicationStopping.Register(() =>
    logger.LogInformation("{AppName} is shutting down (stop signal received)...", appInfo.Options.Name));
app.Lifetime.ApplicationStopped.Register(() =>
    logger.LogInformation("{AppName} stopped cleanly", appInfo.Options.Name));

// ----- HTTP pipeline -----
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
}

app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseStaticFiles();
app.UseRouting();

// Health endpoints (Docker HEALTHCHECK / Kubernetes probes)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthTags.Live),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthTags.Ready),
    ResponseWriter = HealthResponseWriter.WriteAsync,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");

await app.RunAsync();
return 0;
