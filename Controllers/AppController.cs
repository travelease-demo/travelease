using Microsoft.AspNetCore.Mvc;
using TravelEase.Models;

namespace TravelEase.Controllers;

/// <summary>Shared helpers for MVC controllers: toast notifications and modal-form responses.</summary>
public abstract class AppController : Controller
{
    protected void Toast(string message, string type = "success")
    {
        TempData["ToastMessage"] = message;
        TempData["ToastType"] = type;
    }

    /// <summary>JSON response consumed by site.js after a successful modal form submission.</summary>
    protected IActionResult ModalSuccess(string message, string? redirectUrl = null) =>
        Json(new { success = true, message, redirectUrl });

    /// <summary>Re-renders the modal partial with validation errors (HTTP 422 so site.js swaps the content).</summary>
    protected IActionResult ModalInvalid(string partialName, object model)
    {
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView(partialName, model);
    }

    protected void AddError(ServiceResult result)
    {
        ModelState.AddModelError(result.Field ?? string.Empty, result.Error ?? "The operation failed.");
    }
}
