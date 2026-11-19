using Microsoft.AspNetCore.Mvc;
using TravelEase.Models.ViewModels;
using TravelEase.Services;

namespace TravelEase.Controllers;

public class BookingsController(IBookingService bookings, ICustomerService customers, ITripService trips) : AppController
{
    private const string CreatePartial = "_BookingForm";
    private const string CancelPartial = "_CancelForm";

    public async Task<IActionResult> Index([FromQuery] BookingFilter filter, CancellationToken ct)
    {
        ViewData["Title"] = "Bookings";
        return View(await bookings.GetIndexAsync(filter, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var booking = await bookings.GetAsync(id, ct);
        if (booking is null) return NotFound();
        ViewData["Title"] = $"Booking {booking.BookingReference}";
        return View(booking);
    }

    /// <summary>Topbar search: jump straight to a booking by its reference, otherwise search the list.</summary>
    [HttpGet]
    public async Task<IActionResult> Find(string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return RedirectToAction(nameof(Index));
        var booking = await bookings.GetByReferenceAsync(q, ct);
        return booking is not null
            ? RedirectToAction(nameof(Details), new { id = booking.Id })
            : RedirectToAction(nameof(Index), new { search = q });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? customerId, int? tripId, CancellationToken ct) =>
        PartialView(CreatePartial, await BuildFormAsync(new BookingCreateModel
        {
            CustomerId = customerId ?? 0,
            TripId = tripId ?? 0
        }, ct));

    [HttpPost]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] BookingCreateModel input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ModalInvalid(CreatePartial, await BuildFormAsync(input, ct));

        var result = await bookings.CreateAsync(input, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field is null ? string.Empty : $"Input.{result.Field}", result.Error!);
            return ModalInvalid(CreatePartial, await BuildFormAsync(input, ct));
        }

        var booking = result.Value!;
        Toast($"Booking {booking.BookingReference} created ({booking.Status}).");
        return ModalSuccess($"Booking {booking.BookingReference} created.", Url.Action(nameof(Details), new { id = booking.Id }));
    }

    [HttpPost]
    public async Task<IActionResult> Confirm(int id, string? returnUrl, CancellationToken ct)
    {
        var result = await bookings.ConfirmAsync(id, ct);
        if (result.NotFound) return NotFound();
        Toast(result.Succeeded ? $"Booking {result.Value!.BookingReference} confirmed." : result.Error!, result.Succeeded ? "success" : "error");
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action(nameof(Details), new { id })!);
    }

    [HttpGet]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var booking = await bookings.GetAsync(id, ct);
        if (booking is null) return NotFound();
        return PartialView(CancelPartial, new BookingCancelModel { Id = id, BookingReference = booking.BookingReference });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, BookingCancelModel input, CancellationToken ct)
    {
        input.Id = id;
        if (!ModelState.IsValid) return ModalInvalid(CancelPartial, input);

        var result = await bookings.CancelAsync(id, input.Reason, ct);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            AddError(result);
            return ModalInvalid(CancelPartial, input);
        }

        Toast($"Booking {result.Value!.BookingReference} cancelled. {result.Value.Passengers} seat(s) released.", "warning");
        return ModalSuccess($"Booking {result.Value.BookingReference} cancelled.");
    }

    private async Task<BookingFormViewModel> BuildFormAsync(BookingCreateModel input, CancellationToken ct) => new()
    {
        Input = input,
        Customers = await customers.GetSelectListAsync(input.CustomerId, ct),
        Trips = await trips.GetBookableAsync(ct)
    };
}
