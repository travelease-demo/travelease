using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelEase.Data;
using TravelEase.Models;
using TravelEase.Models.ViewModels;

namespace TravelEase.Services;

public interface IBookingService
{
    Task<BookingIndexViewModel> GetIndexAsync(BookingFilter filter, CancellationToken ct = default);
    Task<Booking?> GetAsync(int id, CancellationToken ct = default);
    Task<Booking?> GetByReferenceAsync(string reference, CancellationToken ct = default);
    Task<ServiceResult<Booking>> CreateAsync(BookingCreateModel input, CancellationToken ct = default);
    Task<ServiceResult<Booking>> ConfirmAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<Booking>> CancelAsync(int id, string reason, CancellationToken ct = default);
}

public sealed class BookingService(TravelEaseDbContext db, ILogger<BookingService> logger) : IBookingService
{
    public async Task<BookingIndexViewModel> GetIndexAsync(BookingFilter filter, CancellationToken ct = default)
    {
        filter.Page = Math.Max(1, filter.Page);
        var query = db.Bookings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = $"%{filter.Search.Trim()}%";
            query = query.Where(b =>
                EF.Functions.ILike(b.BookingReference, term) ||
                EF.Functions.ILike(b.Customer!.FirstName + " " + b.Customer.LastName, term) ||
                EF.Functions.ILike(b.Customer!.Email, term) ||
                EF.Functions.ILike(b.Trip!.TripCode, term) ||
                EF.Functions.ILike(b.Trip!.Destination, term) ||
                EF.Functions.ILike(b.Trip!.Source, term));
        }

        if (filter.CustomerId.HasValue) query = query.Where(b => b.CustomerId == filter.CustomerId);
        if (filter.TripId.HasValue) query = query.Where(b => b.TripId == filter.TripId);
        if (filter.From.HasValue)
        {
            var from = DateTime.SpecifyKind(filter.From.Value.Date, DateTimeKind.Utc);
            query = query.Where(b => b.BookingDate >= from);
        }
        if (filter.To.HasValue)
        {
            var to = DateTime.SpecifyKind(filter.To.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(b => b.BookingDate < to);
        }

        // Status counts reflect every filter except the status tab itself.
        var counts = await query
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        if (filter.Status.HasValue) query = query.Where(b => b.Status == filter.Status);

        var total = await query.CountAsync(ct);
        var items = await query
            .Include(b => b.Customer)
            .Include(b => b.Trip)
            .OrderByDescending(b => b.BookingDate)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(ct);

        return new BookingIndexViewModel
        {
            Filter = filter,
            StatusCounts = Enum.GetValues<BookingStatus>().ToDictionary(s => s, s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0),
            Bookings = new PagedResult<Booking> { Items = items, Page = filter.Page, PageSize = filter.PageSize, TotalCount = total }
        };
    }

    public Task<Booking?> GetAsync(int id, CancellationToken ct = default) =>
        db.Bookings.AsNoTracking()
            .Include(b => b.Customer)
            .Include(b => b.Trip)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<Booking?> GetByReferenceAsync(string reference, CancellationToken ct = default) =>
        db.Bookings.AsNoTracking()
            .Include(b => b.Customer)
            .Include(b => b.Trip)
            .FirstOrDefaultAsync(b => b.BookingReference == reference.Trim().ToUpper(), ct);

    public async Task<ServiceResult<Booking>> CreateAsync(BookingCreateModel input, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CustomerId, ct);
        if (customer is null) return ServiceResult<Booking>.Fail("Selected customer does not exist.", nameof(input.CustomerId));

        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == input.TripId, ct);
        if (trip is null) return ServiceResult<Booking>.Fail("Selected trip does not exist.", nameof(input.TripId));
        if (!trip.IsBookable) return ServiceResult<Booking>.Fail($"Trip {trip.TripCode} is not open for booking.", nameof(input.TripId));
        if (trip.AvailableSeats < input.Passengers)
        {
            return ServiceResult<Booking>.Fail($"Only {trip.AvailableSeats} seat(s) left on {trip.TripCode}.", nameof(input.Passengers));
        }

        // The seat decrement and the booking insert must succeed or fail together. The execution
        // strategy wraps the transaction so Npgsql's transient-failure retries stay safe.
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            var booking = await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                // Atomic conditional update: never oversells even with concurrent requests.
                var updated = await db.Trips
                    .Where(t => t.Id == trip.Id && t.AvailableSeats >= input.Passengers)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.AvailableSeats, t => t.AvailableSeats - input.Passengers), ct);

                if (updated == 0) return null;

                var now = DateTime.UtcNow;
                var entity = new Booking
                {
                    BookingReference = await UniqueReferenceAsync(now, ct),
                    CustomerId = customer.Id,
                    TripId = trip.Id,
                    Passengers = input.Passengers,
                    UnitPrice = trip.TicketPrice,
                    TotalPrice = trip.TicketPrice * input.Passengers,
                    Status = input.ConfirmImmediately ? BookingStatus.Confirmed : BookingStatus.Pending,
                    BookingDate = now,
                    ConfirmedAt = input.ConfirmImmediately ? now : null,
                    Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()
                };
                db.Bookings.Add(entity);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return entity;
            });

            if (booking is null)
            {
                return ServiceResult<Booking>.Fail("Those seats were just taken by another booking. Please review availability.", nameof(input.Passengers));
            }

            booking.Customer = customer;
            booking.Trip = trip;

            logger.LogInformation(
                "Booking {BookingReference} created for customer {CustomerId} ({CustomerName}) on trip {TripCode}: {Passengers} passenger(s), total {TotalPrice}, status {Status}",
                booking.BookingReference, customer.Id, customer.FullName, trip.TripCode, booking.Passengers, booking.TotalPrice, booking.Status);

            return ServiceResult<Booking>.Ok(booking);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
            logger.LogError(ex, "Booking creation failed for customer {CustomerId} on trip {TripId}: {SqlState}", input.CustomerId, input.TripId, pg.SqlState);
            return ServiceResult<Booking>.Fail("The booking could not be saved. Please try again.");
        }
    }

    public async Task<ServiceResult<Booking>> ConfirmAsync(int id, CancellationToken ct = default)
    {
        var booking = await db.Bookings.Include(b => b.Trip).Include(b => b.Customer).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return ServiceResult<Booking>.Missing("Booking not found.");
        if (booking.Status != BookingStatus.Pending)
        {
            return ServiceResult<Booking>.Fail($"Only pending bookings can be confirmed. {booking.BookingReference} is {booking.Status}.");
        }
        if (booking.Trip!.Status == TripStatus.Cancelled)
        {
            return ServiceResult<Booking>.Fail($"Trip {booking.Trip.TripCode} has been cancelled; the booking cannot be confirmed.");
        }

        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Booking {BookingReference} confirmed (trip {TripCode}, total {TotalPrice})",
            booking.BookingReference, booking.Trip.TripCode, booking.TotalPrice);
        return ServiceResult<Booking>.Ok(booking);
    }

    public async Task<ServiceResult<Booking>> CancelAsync(int id, string reason, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var booking = await db.Bookings.Include(b => b.Trip).Include(b => b.Customer).FirstOrDefaultAsync(b => b.Id == id, ct);
            if (booking is null) return ServiceResult<Booking>.Missing("Booking not found.");
            if (booking.Status == BookingStatus.Cancelled)
            {
                return ServiceResult<Booking>.Fail($"Booking {booking.BookingReference} is already cancelled.");
            }

            var previousStatus = booking.Status;
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAt = DateTime.UtcNow;
            booking.CancellationReason = reason.Trim();
            await db.SaveChangesAsync(ct);

            // Release the seats back to the trip inventory.
            await db.Trips
                .Where(t => t.Id == booking.TripId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AvailableSeats, t => Math.Min(t.TotalSeats, t.AvailableSeats + booking.Passengers)), ct);

            await tx.CommitAsync(ct);

            logger.LogWarning(
                "Booking {BookingReference} cancelled (was {PreviousStatus}). Trip {TripCode}: {Passengers} seat(s) released. Reason: {Reason}",
                booking.BookingReference, previousStatus, booking.Trip!.TripCode, booking.Passengers, booking.CancellationReason);

            return ServiceResult<Booking>.Ok(booking);
        });
    }

    private async Task<string> UniqueReferenceAsync(DateTime date, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var reference = BookingReferenceGenerator.Create(date);
            if (!await db.Bookings.AnyAsync(b => b.BookingReference == reference, ct)) return reference;
        }
        throw new InvalidOperationException("Unable to generate a unique booking reference.");
    }
}
