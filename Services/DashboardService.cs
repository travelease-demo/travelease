using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TravelEase.Data;
using TravelEase.Models;
using TravelEase.Models.ViewModels;

namespace TravelEase.Services;

public interface IDashboardService
{
    Task<DashboardViewModel> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MonthlyPoint>> GetMonthlyRevenueAsync(int months = 12, CancellationToken ct = default);
}

public sealed class DashboardService(TravelEaseDbContext db) : IDashboardService
{
    public async Task<DashboardViewModel> GetDashboardAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonthStart = monthStart.AddMonths(-1);

        var statusTotals = await db.Bookings
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Revenue = g.Sum(b => b.TotalPrice), Passengers = g.Sum(b => b.Passengers) })
            .ToListAsync(ct);

        int Count(BookingStatus s) => statusTotals.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
        var confirmedRevenue = statusTotals.FirstOrDefault(x => x.Status == BookingStatus.Confirmed)?.Revenue ?? 0;
        var confirmedCount = Count(BookingStatus.Confirmed);

        var revenueThisMonth = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed && b.BookingDate >= monthStart)
            .SumAsync(b => (decimal?)b.TotalPrice, ct) ?? 0;

        var revenueLastMonth = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed && b.BookingDate >= lastMonthStart && b.BookingDate < monthStart)
            .SumAsync(b => (decimal?)b.TotalPrice, ct) ?? 0;

        var recent = await db.Bookings.AsNoTracking()
            .Include(b => b.Customer).Include(b => b.Trip)
            .OrderByDescending(b => b.BookingDate)
            .Take(8)
            .ToListAsync(ct);

        var nextDepartures = await db.Trips.AsNoTracking()
            .Where(t => t.DepartureDate > now && t.Status != TripStatus.Cancelled)
            .OrderBy(t => t.DepartureDate)
            .Take(5)
            .ToListAsync(ct);

        var topDestinationRows = await db.Bookings
            .Where(b => b.Status != BookingStatus.Cancelled)
            .GroupBy(b => b.Trip!.Destination)
            .Select(g => new
            {
                Destination = g.Key,
                Bookings = g.Count(),
                Revenue = g.Sum(b => b.Status == BookingStatus.Confirmed ? b.TotalPrice : 0)
            })
            .OrderByDescending(d => d.Bookings)
            .Take(5)
            .ToListAsync(ct);
        var topDestinations = topDestinationRows.Select(d => new DestinationStat(d.Destination, d.Bookings, d.Revenue)).ToList();

        return new DashboardViewModel
        {
            TotalBookings = statusTotals.Sum(x => x.Count),
            TotalCustomers = await db.Customers.CountAsync(ct),
            TotalTrips = await db.Trips.CountAsync(ct),
            UpcomingTrips = await db.Trips.CountAsync(t => t.DepartureDate > now && t.Status != TripStatus.Cancelled, ct),
            NewCustomersThisMonth = await db.Customers.CountAsync(c => c.CreatedAt >= monthStart, ct),
            TotalRevenue = confirmedRevenue,
            RevenueThisMonth = revenueThisMonth,
            RevenueLastMonth = revenueLastMonth,
            ConfirmedBookings = confirmedCount,
            PendingBookings = Count(BookingStatus.Pending),
            CancelledBookings = Count(BookingStatus.Cancelled),
            TotalPassengers = statusTotals.Where(x => x.Status != BookingStatus.Cancelled).Sum(x => x.Passengers),
            AverageBookingValue = confirmedCount == 0 ? 0 : Math.Round(confirmedRevenue / confirmedCount, 0),
            RecentBookings = recent,
            NextDepartures = nextDepartures,
            TopDestinations = topDestinations,
            MonthlyRevenue = await GetMonthlyRevenueAsync(12, ct)
        };
    }

    public async Task<IReadOnlyList<MonthlyPoint>> GetMonthlyRevenueAsync(int months = 12, CancellationToken ct = default)
    {
        months = Math.Clamp(months, 1, 36);
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));

        var grouped = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed && b.BookingDate >= start)
            .GroupBy(b => new { b.BookingDate.Year, b.BookingDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(b => b.TotalPrice), Count = g.Count() })
            .ToListAsync(ct);

        return Enumerable.Range(0, months)
            .Select(i => start.AddMonths(i))
            .Select(m =>
            {
                var row = grouped.FirstOrDefault(g => g.Year == m.Year && g.Month == m.Month);
                return new MonthlyPoint(m.ToString("MMM yy", CultureInfo.InvariantCulture), m.Year, m.Month, row?.Revenue ?? 0, row?.Count ?? 0);
            })
            .ToList();
    }
}
