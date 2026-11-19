using Microsoft.AspNetCore.Mvc.Rendering;

namespace TravelEase.Models.ViewModels;

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public int TotalCount { get; init; }

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
    public int FirstItem => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastItem => Math.Min(Page * PageSize, TotalCount);
}

// ---------- Customers ----------

public record CustomerListItem(
    int Id, string FullName, string Initials, string Email, string Phone,
    string? City, string? Country, int BookingCount, decimal TotalSpent, DateTime CreatedAt);

public class CustomerIndexViewModel
{
    public PagedResult<CustomerListItem> Customers { get; init; } = new();
    public string? Search { get; init; }
    public string? Country { get; init; }
    public IReadOnlyList<string> Countries { get; init; } = Array.Empty<string>();
}

public class CustomerDetailsViewModel
{
    public required Customer Customer { get; init; }
    public int TotalBookings { get; init; }
    public int ConfirmedBookings { get; init; }
    public int TotalPassengers { get; init; }
    public decimal TotalSpent { get; init; }
}

// ---------- Trips ----------

public class TripFilter
{
    public string? Search { get; set; }
    public TripStatus? Status { get; set; }
    public TransportType? Transport { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class TripIndexViewModel
{
    public PagedResult<Trip> Trips { get; init; } = new();
    public TripFilter Filter { get; init; } = new();
    public int ScheduledCount { get; init; }
    public int TotalSeatsAvailable { get; init; }
}

public class TripDetailsViewModel
{
    public required Trip Trip { get; init; }
    public decimal Revenue { get; init; }
    public int ActiveBookings { get; init; }
}

// ---------- Bookings ----------

public class BookingFilter
{
    public string? Search { get; set; }
    public BookingStatus? Status { get; set; }
    public int? CustomerId { get; set; }
    public int? TripId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

public class BookingIndexViewModel
{
    public PagedResult<Booking> Bookings { get; init; } = new();
    public BookingFilter Filter { get; init; } = new();
    public IReadOnlyDictionary<BookingStatus, int> StatusCounts { get; init; } = new Dictionary<BookingStatus, int>();
    public int AllCount => StatusCounts.Values.Sum();
}

public class BookingFormViewModel
{
    public BookingCreateModel Input { get; init; } = new();
    public IReadOnlyList<SelectListItem> Customers { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<TripOption> Trips { get; init; } = Array.Empty<TripOption>();
}

public record TripOption(int Id, string TripCode, string Route, DateTime DepartureDate, decimal TicketPrice, int AvailableSeats, TransportType TransportType);

// ---------- Dashboard ----------

public record MonthlyPoint(string Label, int Year, int Month, decimal Revenue, int Bookings);
public record DestinationStat(string Destination, int Bookings, decimal Revenue);

public class DashboardViewModel
{
    public int TotalBookings { get; init; }
    public int TotalCustomers { get; init; }
    public int TotalTrips { get; init; }
    public int UpcomingTrips { get; init; }
    public decimal TotalRevenue { get; init; }
    public decimal RevenueThisMonth { get; init; }
    public decimal RevenueLastMonth { get; init; }
    public int ConfirmedBookings { get; init; }
    public int PendingBookings { get; init; }
    public int CancelledBookings { get; init; }
    public int NewCustomersThisMonth { get; init; }
    public int TotalPassengers { get; init; }
    public decimal AverageBookingValue { get; init; }
    public IReadOnlyList<Booking> RecentBookings { get; init; } = Array.Empty<Booking>();
    public IReadOnlyList<Trip> NextDepartures { get; init; } = Array.Empty<Trip>();
    public IReadOnlyList<MonthlyPoint> MonthlyRevenue { get; init; } = Array.Empty<MonthlyPoint>();
    public IReadOnlyList<DestinationStat> TopDestinations { get; init; } = Array.Empty<DestinationStat>();

    public double Percent(int value) => TotalBookings == 0 ? 0 : Math.Round(value * 100.0 / TotalBookings, 1);

    public double? RevenueGrowthPercent => RevenueLastMonth == 0
        ? null
        : Math.Round((double)((RevenueThisMonth - RevenueLastMonth) / RevenueLastMonth * 100), 1);
}

// ---------- Reports ----------

public record MonthlyReportRow(string Label, int Year, int Month, int Bookings, int Confirmed, int Pending, int Cancelled, int Passengers, decimal Revenue);
public record CustomerReportRow(int CustomerId, string Name, string Email, string? Country, int Bookings, int Passengers, decimal Revenue, DateTime? LastBooking);
public record TripReportRow(int TripId, string TripCode, string Route, TransportType Transport, DateTime DepartureDate, TripStatus Status, int TotalSeats, int BookedSeats, double Occupancy, int Bookings, decimal Revenue);
public record StatusReportRow(BookingStatus Status, int Bookings, int Passengers, decimal Value, double Percent);

public class ReportsViewModel
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public decimal TotalRevenue { get; init; }
    public int TotalBookings { get; init; }
    public int TotalPassengers { get; init; }
    public decimal AverageBookingValue { get; init; }
    public double CancellationRate { get; init; }
    public IReadOnlyList<MonthlyReportRow> Monthly { get; init; } = Array.Empty<MonthlyReportRow>();
    public IReadOnlyList<CustomerReportRow> Customers { get; init; } = Array.Empty<CustomerReportRow>();
    public IReadOnlyList<TripReportRow> Trips { get; init; } = Array.Empty<TripReportRow>();
    public IReadOnlyList<StatusReportRow> Statuses { get; init; } = Array.Empty<StatusReportRow>();
    public StorageInfo Storage { get; init; } = new();
}

public record ReportFileInfo(string Name, long SizeBytes, DateTime CreatedUtc);

public class StorageInfo
{
    public string Path { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public bool Writable { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<ReportFileInfo> Files { get; init; } = Array.Empty<ReportFileInfo>();
    public long TotalBytes => Files.Sum(f => f.SizeBytes);
}

// ---------- Settings ----------

public record HealthEntryView(string Name, string Status, string? Description, double DurationMs, IReadOnlyList<string> Tags);
public record ConfigEntry(string Key, string Value, bool Masked);

public class SettingsViewModel
{
    public string AppName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string HostName { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string Framework { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string ProcessUser { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public bool RunningInContainer { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public TimeSpan Uptime { get; init; }
    public string ContentRoot { get; init; } = string.Empty;
    public IReadOnlyList<string> ListeningUrls { get; init; } = Array.Empty<string>();
    public long WorkingSetMb { get; init; }

    public bool DatabaseConnected { get; init; }
    public string DatabaseHost { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public string? DatabaseServerVersion { get; init; }
    public double? DatabaseLatencyMs { get; init; }
    public string? DatabaseError { get; init; }
    public string MaskedConnectionString { get; init; } = string.Empty;
    public IReadOnlyList<string> AppliedMigrations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PendingMigrations { get; init; } = Array.Empty<string>();
    public string DatabaseInitState { get; init; } = string.Empty;

    public string OverallHealth { get; init; } = string.Empty;
    public IReadOnlyList<HealthEntryView> HealthEntries { get; init; } = Array.Empty<HealthEntryView>();

    public StorageInfo ReportStorage { get; init; } = new();
    public IReadOnlyList<ConfigEntry> Configuration { get; init; } = Array.Empty<ConfigEntry>();
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public int StatusCode { get; set; } = 500;
    public string Title { get; set; } = "Something went wrong";
    public string Message { get; set; } = "An unexpected error occurred while processing your request.";
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
