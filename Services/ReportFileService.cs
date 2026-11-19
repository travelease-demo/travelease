using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using TravelEase.Configuration;
using TravelEase.Models.ViewModels;

namespace TravelEase.Services;

public enum ReportType
{
    Monthly,
    Customers,
    Trips,
    Status
}

/// <summary>
/// Writes generated CSV reports to the configured reports directory (Storage:ReportsPath).
/// In a container, mount a Docker volume at that path so files survive container re-creation.
/// </summary>
public interface IReportFileService
{
    string ReportsDirectory { get; }
    Task<string> ExportAsync(ReportType type, ReportsViewModel data, CancellationToken ct = default);
    StorageInfo GetStorageInfo();
    string? GetFilePath(string fileName);
    bool Delete(string fileName);
}

public sealed class ReportFileService : IReportFileService
{
    private readonly ILogger<ReportFileService> _logger;

    public ReportFileService(IOptions<StorageOptions> options, IWebHostEnvironment environment, ILogger<ReportFileService> logger)
    {
        _logger = logger;
        var configured = options.Value.ReportsPath;
        ReportsDirectory = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured));
    }

    public string ReportsDirectory { get; }

    public async Task<string> ExportAsync(ReportType type, ReportsViewModel data, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ReportsDirectory);

        var fileName = $"{type.ToString().ToLowerInvariant()}-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        var path = Path.Combine(ReportsDirectory, fileName);

        var csv = new StringBuilder();
        switch (type)
        {
            case ReportType.Monthly:
                csv.AppendLine("Month,Bookings,Confirmed,Pending,Cancelled,Passengers,Revenue");
                foreach (var r in data.Monthly)
                    csv.AppendLine(Row(r.Label, r.Bookings, r.Confirmed, r.Pending, r.Cancelled, r.Passengers, r.Revenue));
                break;
            case ReportType.Customers:
                csv.AppendLine("CustomerId,Name,Email,Country,Bookings,Passengers,Revenue,LastBookingUtc");
                foreach (var r in data.Customers)
                    csv.AppendLine(Row(r.CustomerId, r.Name, r.Email, r.Country, r.Bookings, r.Passengers, r.Revenue, r.LastBooking?.ToString("u")));
                break;
            case ReportType.Trips:
                csv.AppendLine("TripCode,Route,Transport,DepartureUtc,Status,TotalSeats,BookedSeats,OccupancyPercent,Bookings,Revenue");
                foreach (var r in data.Trips)
                    csv.AppendLine(Row(r.TripCode, r.Route.Replace("→", "->"), r.Transport, r.DepartureDate.ToString("u"), r.Status, r.TotalSeats, r.BookedSeats, r.Occupancy, r.Bookings, r.Revenue));
                break;
            case ReportType.Status:
                csv.AppendLine("Status,Bookings,Passengers,Value,Percent");
                foreach (var r in data.Statuses)
                    csv.AppendLine(Row(r.Status, r.Bookings, r.Passengers, r.Value, r.Percent));
                break;
        }

        await File.WriteAllTextAsync(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
        _logger.LogInformation("Report {ReportType} exported to {ReportPath} ({Bytes} bytes)", type, path, new FileInfo(path).Length);
        return fileName;
    }

    public StorageInfo GetStorageInfo()
    {
        try
        {
            var exists = Directory.Exists(ReportsDirectory);
            if (!exists)
            {
                return new StorageInfo { Path = ReportsDirectory, Exists = false, Writable = CanWrite() };
            }

            var files = new DirectoryInfo(ReportsDirectory)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => new ReportFileInfo(Path.GetRelativePath(ReportsDirectory, f.FullName).Replace('\\', '/'), f.Length, f.LastWriteTimeUtc))
                .ToList();

            return new StorageInfo { Path = ReportsDirectory, Exists = true, Writable = CanWrite(), Files = files };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to read report storage at {ReportPath}", ReportsDirectory);
            return new StorageInfo { Path = ReportsDirectory, Error = ex.Message };
        }
    }

    /// <summary>Resolves a file name safely inside the reports directory (blocks path traversal).</summary>
    public string? GetFilePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var full = Path.GetFullPath(Path.Combine(ReportsDirectory, fileName));
        var root = ReportsDirectory.EndsWith(Path.DirectorySeparatorChar) ? ReportsDirectory : ReportsDirectory + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full) ? full : null;
    }

    public bool Delete(string fileName)
    {
        var path = GetFilePath(fileName);
        if (path is null) return false;
        File.Delete(path);
        _logger.LogInformation("Report file {ReportFile} deleted", fileName);
        return true;
    }

    /// <summary>Checks write permission by creating and removing a probe file (important for non-root containers).</summary>
    public bool CanWrite()
    {
        try
        {
            Directory.CreateDirectory(ReportsDirectory);
            var probe = Path.Combine(ReportsDirectory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Row(params object?[] values) =>
        string.Join(',', values.Select(v => Escape(Convert.ToString(v, CultureInfo.InvariantCulture))));

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
