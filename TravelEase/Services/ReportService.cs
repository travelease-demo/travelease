using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TravelEase.Data;
using TravelEase.Models;
using TravelEase.Models.ViewModels;

namespace TravelEase.Services;

public interface IReportService
{
    Task<ReportsViewModel> GetReportsAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
}

public sealed class ReportService(TravelEaseDbContext db, IReportFileService files) : IReportService
{
    public async Task<ReportsViewModel> GetReportsAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var bookings = db.Bookings.AsNoTracking();
        DateTime? fromUtc = from.HasValue ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) : null;
        DateTime? toUtc = to.HasValue ? DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc) : null;
        if (fromUtc.HasValue) bookings = bookings.Where(b => b.BookingDate >= fromUtc);
        if (toUtc.HasValue) bookings = bookings.Where(b => b.BookingDate < toUtc);

        // ----- Status report -----
        var statusRows = await bookings
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Passengers = g.Sum(b => b.Passengers), Value = g.Sum(b => b.TotalPrice) })
            .ToListAsync(ct);

        var totalBookings = statusRows.Sum(s => s.Count);
        var statuses = Enum.GetValues<BookingStatus>()
            .Select(s =>
            {
                var row = statusRows.FirstOrDefault(r => r.Status == s);
                var count = row?.Count ?? 0;
                return new StatusReportRow(s, count, row?.Passengers ?? 0, row?.Value ?? 0,
                    totalBookings == 0 ? 0 : Math.Round(count * 100.0 / totalBookings, 1));
            })
            .ToList();

        var confirmed = statuses.First(s => s.Status == BookingStatus.Confirmed);
        var cancelled = statuses.First(s => s.Status == BookingStatus.Cancelled);

        // ----- Monthly report (12 months ending at the "to" date or now) -----
        var end = (to ?? DateTime.UtcNow).Date;
        var monthStart = new DateTime(end.Year, end.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);
        if (fromUtc.HasValue && fromUtc.Value > monthStart) monthStart = new DateTime(fromUtc.Value.Year, fromUtc.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var monthlyRaw = await bookings
            .Where(b => b.BookingDate >= monthStart)
            .GroupBy(b => new { b.BookingDate.Year, b.BookingDate.Month, b.Status })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Status, Count = g.Count(), Passengers = g.Sum(b => b.Passengers), Value = g.Sum(b => b.TotalPrice) })
            .ToListAsync(ct);

        var monthly = new List<MonthlyReportRow>();
        for (var m = monthStart; m <= end; m = m.AddMonths(1))
        {
            var rows = monthlyRaw.Where(r => r.Year == m.Year && r.Month == m.Month).ToList();
            int C(BookingStatus s) => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0;
            monthly.Add(new MonthlyReportRow(
                m.ToString("MMM yyyy", CultureInfo.InvariantCulture), m.Year, m.Month,
                rows.Sum(r => r.Count), C(BookingStatus.Confirmed), C(BookingStatus.Pending), C(BookingStatus.Cancelled),
                rows.Where(r => r.Status != BookingStatus.Cancelled).Sum(r => r.Passengers),
                rows.Where(r => r.Status == BookingStatus.Confirmed).Sum(r => r.Value)));
        }

        // ----- Customer report -----
        var customerRows = await bookings
            .GroupBy(b => new { b.CustomerId, b.Customer!.FirstName, b.Customer.LastName, b.Customer.Email, b.Customer.Country })
            .Select(g => new
            {
                g.Key.CustomerId, g.Key.FirstName, g.Key.LastName, g.Key.Email, g.Key.Country,
                Bookings = g.Count(),
                Passengers = g.Where(b => b.Status != BookingStatus.Cancelled).Sum(b => b.Passengers),
                Revenue = g.Where(b => b.Status == BookingStatus.Confirmed).Sum(b => b.TotalPrice),
                LastBooking = g.Max(b => b.BookingDate)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(15)
            .ToListAsync(ct);

        // ----- Trip report -----
        var tripRows = await db.Trips.AsNoTracking()
            .Select(t => new
            {
                t.Id, t.TripCode, t.Source, t.Destination, t.TransportType, t.DepartureDate, t.Status, t.TotalSeats, t.AvailableSeats,
                Bookings = t.Bookings.Count(b => (!fromUtc.HasValue || b.BookingDate >= fromUtc) && (!toUtc.HasValue || b.BookingDate < toUtc)),
                Revenue = t.Bookings
                    .Where(b => b.Status == BookingStatus.Confirmed && (!fromUtc.HasValue || b.BookingDate >= fromUtc) && (!toUtc.HasValue || b.BookingDate < toUtc))
                    .Sum(b => (decimal?)b.TotalPrice) ?? 0
            })
            .OrderByDescending(t => t.Revenue)
            .ToListAsync(ct);

        return new ReportsViewModel
        {
            From = from,
            To = to,
            TotalRevenue = confirmed.Value,
            TotalBookings = totalBookings,
            TotalPassengers = statuses.Where(s => s.Status != BookingStatus.Cancelled).Sum(s => s.Passengers),
            AverageBookingValue = confirmed.Bookings == 0 ? 0 : Math.Round(confirmed.Value / confirmed.Bookings, 0),
            CancellationRate = cancelled.Percent,
            Statuses = statuses,
            Monthly = monthly,
            Customers = customerRows.Select(c => new CustomerReportRow(c.CustomerId, $"{c.FirstName} {c.LastName}", c.Email, c.Country, c.Bookings, c.Passengers, c.Revenue, c.LastBooking)).ToList(),
            Trips = tripRows.Select(t =>
            {
                var booked = t.TotalSeats - t.AvailableSeats;
                return new TripReportRow(t.Id, t.TripCode, $"{t.Source} → {t.Destination}", t.TransportType, t.DepartureDate, t.Status,
                    t.TotalSeats, booked, t.TotalSeats == 0 ? 0 : Math.Round(booked * 100.0 / t.TotalSeats, 1), t.Bookings, t.Revenue);
            }).ToList(),
            Storage = files.GetStorageInfo()
        };
    }
}
