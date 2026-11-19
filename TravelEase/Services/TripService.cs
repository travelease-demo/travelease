using Microsoft.EntityFrameworkCore;
using TravelEase.Data;
using TravelEase.Models;
using TravelEase.Models.ViewModels;

namespace TravelEase.Services;

public interface ITripService
{
    Task<TripIndexViewModel> GetIndexAsync(TripFilter filter, CancellationToken ct = default);
    Task<TripDetailsViewModel?> GetDetailsAsync(int id, CancellationToken ct = default);
    Task<Trip?> FindAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<Trip>> CreateAsync(TripFormModel input, CancellationToken ct = default);
    Task<ServiceResult<Trip>> UpdateAsync(int id, TripFormModel input, CancellationToken ct = default);
    Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<TripOption>> GetBookableAsync(CancellationToken ct = default);
}

public sealed class TripService(TravelEaseDbContext db, ILogger<TripService> logger) : ITripService
{
    public async Task<TripIndexViewModel> GetIndexAsync(TripFilter filter, CancellationToken ct = default)
    {
        filter.Page = Math.Max(1, filter.Page);
        var query = db.Trips.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = $"%{filter.Search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.TripCode, term) ||
                EF.Functions.ILike(t.Source, term) ||
                EF.Functions.ILike(t.Destination, term) ||
                (t.Operator != null && EF.Functions.ILike(t.Operator, term)));
        }

        if (filter.Status.HasValue) query = query.Where(t => t.Status == filter.Status);
        if (filter.Transport.HasValue) query = query.Where(t => t.TransportType == filter.Transport);
        if (filter.From.HasValue)
        {
            var from = DateTime.SpecifyKind(filter.From.Value.Date, DateTimeKind.Utc);
            query = query.Where(t => t.DepartureDate >= from);
        }
        if (filter.To.HasValue)
        {
            var to = DateTime.SpecifyKind(filter.To.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(t => t.DepartureDate < to);
        }

        var total = await query.CountAsync(ct);

        // Upcoming trips first (soonest departure), then past trips (most recent first).
        var now = DateTime.UtcNow;
        var items = await query
            .OrderBy(t => t.DepartureDate < now)
            .ThenBy(t => t.DepartureDate >= now ? t.DepartureDate : (DateTime?)null)
            .ThenByDescending(t => t.DepartureDate)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(ct);

        var scheduled = await db.Trips.CountAsync(t => (t.Status == TripStatus.Scheduled || t.Status == TripStatus.Delayed) && t.DepartureDate > now, ct);
        var seats = await db.Trips
            .Where(t => (t.Status == TripStatus.Scheduled || t.Status == TripStatus.Delayed) && t.DepartureDate > now)
            .SumAsync(t => (int?)t.AvailableSeats, ct) ?? 0;

        return new TripIndexViewModel
        {
            Filter = filter,
            ScheduledCount = scheduled,
            TotalSeatsAvailable = seats,
            Trips = new PagedResult<Trip> { Items = items, Page = filter.Page, PageSize = filter.PageSize, TotalCount = total }
        };
    }

    public async Task<TripDetailsViewModel?> GetDetailsAsync(int id, CancellationToken ct = default)
    {
        var trip = await db.Trips.AsNoTracking()
            .Include(t => t.Bookings.OrderByDescending(b => b.BookingDate))
            .ThenInclude(b => b.Customer)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (trip is null) return null;

        return new TripDetailsViewModel
        {
            Trip = trip,
            Revenue = trip.Bookings.Where(b => b.Status == BookingStatus.Confirmed).Sum(b => b.TotalPrice),
            ActiveBookings = trip.Bookings.Count(b => b.Status != BookingStatus.Cancelled)
        };
    }

    public Task<Trip?> FindAsync(int id, CancellationToken ct = default) =>
        db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<ServiceResult<Trip>> CreateAsync(TripFormModel input, CancellationToken ct = default)
    {
        var code = input.TripCode.Trim().ToUpperInvariant();
        if (await db.Trips.AnyAsync(t => t.TripCode == code, ct))
        {
            return ServiceResult<Trip>.Fail("A trip with this code already exists.", nameof(input.TripCode));
        }

        var trip = new Trip { AvailableSeats = input.TotalSeats };
        Apply(trip, input, code);
        db.Trips.Add(trip);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Trip {TripCode} created: {Source} -> {Destination} departing {DepartureDate:u}, {Seats} seats at {Price}",
            trip.TripCode, trip.Source, trip.Destination, trip.DepartureDate, trip.TotalSeats, trip.TicketPrice);
        return ServiceResult<Trip>.Ok(trip);
    }

    public async Task<ServiceResult<Trip>> UpdateAsync(int id, TripFormModel input, CancellationToken ct = default)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trip is null) return ServiceResult<Trip>.Missing("Trip not found.");

        var code = input.TripCode.Trim().ToUpperInvariant();
        if (await db.Trips.AnyAsync(t => t.TripCode == code && t.Id != id, ct))
        {
            return ServiceResult<Trip>.Fail("Another trip already uses this code.", nameof(input.TripCode));
        }

        // Seats already held by pending/confirmed bookings cannot be removed.
        var bookedSeats = await db.Bookings
            .Where(b => b.TripId == id && b.Status != BookingStatus.Cancelled)
            .SumAsync(b => (int?)b.Passengers, ct) ?? 0;

        if (input.TotalSeats < bookedSeats)
        {
            return ServiceResult<Trip>.Fail($"Total seats cannot be lower than the {bookedSeats} seats already booked.", nameof(input.TotalSeats));
        }

        Apply(trip, input, code);
        trip.AvailableSeats = input.TotalSeats - bookedSeats;
        trip.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Trip {TripCode} updated (status {Status}, {Available}/{Total} seats available)",
            trip.TripCode, trip.Status, trip.AvailableSeats, trip.TotalSeats);
        return ServiceResult<Trip>.Ok(trip);
    }

    public async Task<ServiceResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trip is null) return ServiceResult.Missing("Trip not found.");

        var bookings = await db.Bookings.CountAsync(b => b.TripId == id, ct);
        if (bookings > 0)
        {
            return ServiceResult.Fail($"Trip {trip.TripCode} has {bookings} booking(s) and cannot be deleted. Set its status to Cancelled instead.");
        }

        db.Trips.Remove(trip);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Trip {TripCode} deleted", trip.TripCode);
        return ServiceResult.Ok();
    }

    public async Task<IReadOnlyList<TripOption>> GetBookableAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await db.Trips.AsNoTracking()
            .Where(t => (t.Status == TripStatus.Scheduled || t.Status == TripStatus.Delayed) && t.DepartureDate > now && t.AvailableSeats > 0)
            .OrderBy(t => t.DepartureDate)
            .Select(t => new TripOption(t.Id, t.TripCode, t.Source + " → " + t.Destination, t.DepartureDate, t.TicketPrice, t.AvailableSeats, t.TransportType))
            .ToListAsync(ct);
    }

    private static void Apply(Trip trip, TripFormModel input, string code)
    {
        trip.TripCode = code;
        trip.Source = input.Source.Trim();
        trip.Destination = input.Destination.Trim();
        trip.TransportType = input.TransportType;
        trip.Operator = string.IsNullOrWhiteSpace(input.Operator) ? null : input.Operator.Trim();
        trip.DepartureDate = input.DepartureDate;
        trip.ArrivalDate = input.ArrivalDate;
        trip.TotalSeats = input.TotalSeats;
        trip.TicketPrice = input.TicketPrice;
        trip.Status = input.Status;
        trip.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
    }
}
