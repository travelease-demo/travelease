using Microsoft.AspNetCore.Mvc;
using TravelEase.Models.ViewModels;
using TravelEase.Services;

namespace TravelEase.Controllers;

public class CustomersController(ICustomerService customers) : AppController
{
    private const string FormPartial = "_CustomerForm";

    public async Task<IActionResult> Index(string? search, string? country, int page = 1, CancellationToken ct = default)
    {
        ViewData["Title"] = "Customers";
        return View(await customers.GetIndexAsync(search, country, page, 10, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await customers.GetDetailsAsync(id, ct);
        if (model is null) return NotFound();
        ViewData["Title"] = model.Customer.FullName;
        return View(model);
    }

    [HttpGet]
    public IActionResult Create() => PartialView(FormPartial, new CustomerFormModel());

    [HttpPost]
    public async Task<IActionResult> Create(CustomerFormModel input, CancellationToken ct)
    {
        input.Id = null;
        if (!ModelState.IsValid) return ModalInvalid(FormPartial, input);

        var result = await customers.CreateAsync(input, ct);
        if (!result.Succeeded)
        {
            AddError(result);
            return ModalInvalid(FormPartial, input);
        }

        Toast($"Customer {result.Value!.FullName} added.");
        return ModalSuccess($"Customer {result.Value.FullName} added.", Url.Action(nameof(Details), new { id = result.Value.Id }));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var customer = await customers.FindAsync(id, ct);
        return customer is null ? NotFound() : PartialView(FormPartial, CustomerFormModel.From(customer));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, CustomerFormModel input, CancellationToken ct)
    {
        input.Id = id;
        if (!ModelState.IsValid) return ModalInvalid(FormPartial, input);

        var result = await customers.UpdateAsync(id, input, ct);
        if (result.NotFound) return NotFound();
        if (!result.Succeeded)
        {
            AddError(result);
            return ModalInvalid(FormPartial, input);
        }

        Toast($"Customer {result.Value!.FullName} updated.");
        return ModalSuccess($"Customer {result.Value.FullName} updated.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, string? returnUrl, CancellationToken ct)
    {
        var result = await customers.DeleteAsync(id, ct);
        if (result.Succeeded)
        {
            Toast("Customer deleted.");
            return RedirectToAction(nameof(Index));
        }

        Toast(result.Error!, "error");
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action(nameof(Index))!);
    }
}
