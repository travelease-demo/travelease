using System.Globalization;
using Microsoft.Extensions.Options;
using TravelEase.Configuration;
using TravelEase.Models;

namespace TravelEase.Infrastructure;

/// <summary>Formatting helpers injected into Razor views (@Fmt.Money(...), @Fmt.Date(...)).</summary>
public sealed class UiFormatter
{
    private readonly CultureInfo _culture;

    public UiFormatter(IOptions<AppOptions> options, ILogger<UiFormatter> logger)
    {
        Options = options.Value;
        Version = AppVersion.Resolve(Options);
        try
        {
            _culture = CultureInfo.GetCultureInfo(Options.Culture);
        }
        catch (CultureNotFoundException)
        {
            // Happens on images running with DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true (e.g. Alpine without ICU).
            logger.LogWarning("Culture {Culture} is unavailable (invariant globalization mode?); using invariant formatting", Options.Culture);
            _culture = CultureInfo.InvariantCulture;
        }
    }

    public AppOptions Options { get; }
    public string Version { get; }
    public string Currency => Options.CurrencySymbol;

    public string Money(decimal value) => $"{Options.CurrencySymbol}{value.ToString("N0", _culture)}";
    public string MoneyPrecise(decimal value) => $"{Options.CurrencySymbol}{value.ToString("N2", _culture)}";
    public string Number(int value) => value.ToString("N0", _culture);

    public string CompactMoney(decimal value) => value switch
    {
        >= 10_000_000 => $"{Options.CurrencySymbol}{(value / 10_000_000m).ToString("0.##", CultureInfo.InvariantCulture)} Cr",
        >= 100_000 => $"{Options.CurrencySymbol}{(value / 100_000m).ToString("0.##", CultureInfo.InvariantCulture)} L",
        _ => Money(value)
    };

    public string Date(DateTime value) => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public string DateTimeShort(DateTime value) => value.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
    public string Time(DateTime value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string Date(DateTime? value) => value.HasValue ? Date(value.Value) : "—";
    public string DateTimeShort(DateTime? value) => value.HasValue ? DateTimeShort(value.Value) : "—";

    public string Duration(TimeSpan span) => span.TotalDays >= 1
        ? $"{(int)span.TotalDays}d {span.Hours}h"
        : $"{(int)span.TotalHours}h {span.Minutes:00}m";

    public string Relative(DateTime utc)
    {
        var diff = DateTime.UtcNow - utc;
        if (diff.TotalSeconds < 0) return "upcoming";
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}d ago";
        return Date(utc);
    }

    public string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} MB"
    };

    public static string StatusClass(BookingStatus status) => status switch
    {
        BookingStatus.Confirmed => "status-confirmed",
        BookingStatus.Pending => "status-pending",
        BookingStatus.Cancelled => "status-cancelled",
        _ => "status-neutral"
    };

    public static string StatusIcon(BookingStatus status) => status switch
    {
        BookingStatus.Confirmed => "bi-check-circle-fill",
        BookingStatus.Pending => "bi-hourglass-split",
        BookingStatus.Cancelled => "bi-x-circle-fill",
        _ => "bi-circle"
    };

    public static string TripStatusClass(TripStatus status) => status switch
    {
        TripStatus.Scheduled => "status-scheduled",
        TripStatus.Delayed => "status-pending",
        TripStatus.Completed => "status-neutral",
        TripStatus.Cancelled => "status-cancelled",
        _ => "status-neutral"
    };

    public static string TransportIcon(TransportType type) => type switch
    {
        TransportType.Flight => "bi-airplane-fill",
        TransportType.Train => "bi-train-front-fill",
        TransportType.Bus => "bi-bus-front-fill",
        TransportType.Cruise => "bi-water",
        _ => "bi-geo-alt-fill"
    };

    public static string EnvironmentClass(string environment) => environment.ToLowerInvariant() switch
    {
        "development" => "env-dev",
        "staging" => "env-staging",
        "production" => "env-prod",
        _ => "env-other"
    };

    /// <summary>Stable avatar colour per name so the same customer always gets the same tint.</summary>
    public static string AvatarClass(string seed)
    {
        var hash = 0;
        foreach (var ch in seed) hash = (hash * 31 + ch) & 0x7FFFFFFF;
        return $"avatar-{hash % 6}";
    }
}
