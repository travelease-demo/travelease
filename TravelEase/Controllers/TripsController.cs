using Microsoft.AspNetCore.Mvc;
using TravelEase.Models.ViewModels;
using TravelEase.Services;

namespace TravelEase.Controllers;

public class TripsController(ITripService trips) : AppController
{
    private const string FormPartial = "_TripForm";

    public async Task<IActionResult> Index([FromQuery] TripFilter filter, CancellationToken ct)
    {
        ViewData["Title"] = "Trips";
        return View(await trips.GetIndexAsync(filter, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await trips.GetDetailsAsync(id, ct);
        if (model is null) return NotFound();
        ViewData["Title"] = $"Trip {model.Trip.TripCode}";
        return View(model);
    }

    [HttpGet]
    public IActionResult Create() => PartialView(FormPartial, new TripFormModel
    {
        TripCode = $"TE-FL-{Random.Shared.Next(2000, 9999)}"
    });

    [HttpPost]
    public async Task<IActionResult> Create(TripFormModel input, CancellationToken ct)
    {
        input.Id = null;
        if (!ModelState.IsValid) return ModalInvalid(FormPartial, input);

        var result = await trips.CreateAsync(input, ct);
        if (!result.Succeeded)
        {
            AddError(result);
            return ModalInvalid(FormPartial, input);
        }

        Toast($"Trip {result.Value!.TripCode} created.");
        return ModalSuccess($"Trip {result.Value.TripCode} created.", Url.Action(nameof(Details), new { id = result.Value.Id }));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var trip = await trips.FindAsync(id, ct);
        return trip is null ? NotFound() : PartialView(FormPartial, TripFormModel.From(trip));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, TripFormModel input, CancellationToken ct)
    {
        input.Id = id;
        if (!ModelState.IsValid) return ModalInvalid(FormPartial, input);

        var result = await trips.UpdateAsync(id, input, ct);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            AddError(result);
            return ModalInvalid(FormPartial, input);
        }

        Toast($"Trip {result.Value!.TripCode} updated.");
        return ModalSuccess($"Trip {result.Value.TripCode} updated.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, string? returnUrl, CancellationToken ct)
    {
        var result = await trips.DeleteAsync(id, ct);
        if (result.Succeeded)
        {
            Toast("Trip deleted.");
            return RedirectToAction(nameof(Index));
        }

        Toast(result.Error!, "error");
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action(nameof(Index))!);
    }
}
