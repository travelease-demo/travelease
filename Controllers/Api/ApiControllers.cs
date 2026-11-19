using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TravelEase.Configuration;
using TravelEase.Infrastructure;
using TravelEase.Models;
using TravelEase.Models.ViewModels;
using TravelEase.Services;

namespace TravelEase.Controllers.Api;

// ---------------------------------------------------------------------------------------------
// REST API. Handy for testing a container from the command line, e.g.:
//   curl http://localhost:8080/api/info
//   curl http://localhost:8080/api/bookings?status=Pending
//   curl -X POST http://localhost:8080/api/bookings -H "Content-Type: application/json" \
//        -d '{"customerId":1,"tripId":30,"passengers":2,"confirmImmediately":true}'
// ---------------------------------------------------------------------------------------------

public record CustomerDto(int Id, string FirstName, string LastName, string Email, string Phone, string? City, string? Country, DateTime CreatedAt);
public record TripDto(int Id, string TripCode, string Source, string Destination, string TransportType, string? Operator,
    DateTime DepartureDate, DateTime ArrivalDate, int TotalSeats, int AvailableSeats, decimal TicketPrice, string Status);
public record BookingDto(int Id, string BookingReference, int CustomerId, string? CustomerName, int TripId, string? TripCode, string? Route,
    int Passengers, decimal UnitPrice, decimal TotalPrice, string Status, DateTime BookingDate, DateTime? ConfirmedAt, DateTime? CancelledAt, string? CancellationReason);
public record CancelRequest(string Reason);
public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

internal static class Map
{
    public static CustomerDto ToDto(this Customer c) => new(c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.City, c.Country, c.CreatedAt);

    public static TripDto ToDto(this Trip t) => new(t.Id, t.TripCode, t.Source, t.Destination, t.TransportType.ToString(), t.Operator,
        t.DepartureDate, t.ArrivalDate, t.TotalSeats, t.AvailableSeats, t.TicketPrice, t.Status.ToString());

    public static BookingDto ToDto(this Booking b) => new(b.Id, b.BookingReference, b.CustomerId, b.Customer?.FullName, b.TripId, b.Trip?.TripCode,
        b.Trip?.Route, b.Passengers, b.UnitPrice, b.TotalPrice, b.Status.ToString(), b.BookingDate, b.ConfirmedAt, b.CancelledAt, b.CancellationReason);

    public static PagedResponse<TOut> ToResponse<TIn, TOut>(this PagedResult<TIn> page, Func<TIn, TOut> map) =>
        new(page.Items.Select(map).ToList(), page.Page, page.PageSize, page.TotalCount, page.TotalPages);
}

[ApiController]
[IgnoreAntiforgeryToken]
[Route("api")]
public class InfoApiController(IOptions<AppOptions> options, IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>Which instance / version / environment served this request (useful with several containers).</summary>
    [HttpGet("info")]
    public IActionResult Info() => Ok(new
    {
        application = options.Value.Name,
        version = AppVersion.Resolve(options.Value),
        environment = environment.EnvironmentName,
        host = Environment.MachineName,
        user = Environment.UserName,
        runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        timestampUtc = DateTime.UtcNow
    });
}

[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/dashboard")]
public class DashboardApiController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var d = await dashboard.GetDashboardAsync(ct);
        return Ok(new
        {
            d.TotalBookings, d.TotalCustomers, d.TotalTrips, d.UpcomingTrips, d.TotalRevenue,
            d.ConfirmedBookings, d.PendingBookings, d.CancelledBookings, d.TotalPassengers, d.AverageBookingValue
        });
    }

    [HttpGet("monthly-revenue")]
    public async Task<IActionResult> MonthlyRevenue([FromQuery] int months = 12, CancellationToken ct = default) =>
        Ok(await dashboard.GetMonthlyRevenueAsync(months, ct));
}

[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/customers")]
public class CustomersApiController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await customers.GetIndexAsync(search, null, page, Math.Clamp(pageSize, 1, 100), ct);
        return Ok(result.Customers);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var customer = await customers.FindAsync(id, ct);
        return customer is null ? NotFound() : Ok(customer.ToDto());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CustomerFormModel input, CancellationToken ct)
    {
        var result = await customers.CreateAsync(input, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return ValidationProblem(ModelState);
        }
        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value.ToDto());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await customers.DeleteAsync(id, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? NoContent() : Conflict(new { error = result.Error });
    }
}

[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/trips")]
public class TripsApiController(ITripService trips) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] TripFilter filter, CancellationToken ct)
    {
        filter.PageSize = Math.Clamp(filter.PageSize, 1, 100);
        var result = await trips.GetIndexAsync(filter, ct);
        return Ok(result.Trips.ToResponse(t => t.ToDto()));
    }

    [HttpGet("bookable")]
    public async Task<IActionResult> Bookable(CancellationToken ct) => Ok(await trips.GetBookableAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var trip = await trips.FindAsync(id, ct);
        return trip is null ? NotFound() : Ok(trip.ToDto());
    }
}

[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/bookings")]
public class BookingsApiController(IBookingService bookings) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] BookingFilter filter, CancellationToken ct)
    {
        filter.PageSize = Math.Clamp(filter.PageSize, 1, 100);
        var result = await bookings.GetIndexAsync(filter, ct);
        return Ok(result.Bookings.ToResponse(b => b.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var booking = await bookings.GetAsync(id, ct);
        return booking is null ? NotFound() : Ok(booking.ToDto());
    }

    [HttpGet("ref/{reference}")]
    public async Task<IActionResult> GetByReference(string reference, CancellationToken ct)
    {
        var booking = await bookings.GetByReferenceAsync(reference, ct);
        return booking is null ? NotFound() : Ok(booking.ToDto());
    }

    [HttpPost]
    public async Task<IActionResult> Create(BookingCreateModel input, CancellationToken ct)
    {
        var result = await bookings.CreateAsync(input, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return ValidationProblem(ModelState);
        }
        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value.ToDto());
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        var result = await bookings.ConfirmAsync(id, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? Ok(result.Value!.ToDto()) : Conflict(new { error = result.Error });
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancelRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { error = "A cancellation reason is required." });
        var result = await bookings.CancelAsync(id, request.Reason, ct);
        if (result.NotFound) return NotFound();
        return result.Succeeded ? Ok(result.Value!.ToDto()) : Conflict(new { error = result.Error });
    }
}
