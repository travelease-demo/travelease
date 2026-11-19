using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TravelEase.Models.ViewModels;

namespace TravelEase.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public class ErrorController(ILogger<ErrorController> logger) : Controller
{
    [Route("/error")]
    [IgnoreAntiforgeryToken]
    public IActionResult Index()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (feature?.Error is { } error)
        {
            logger.LogError(error, "Unhandled exception on {Method} {Path} (request {RequestId})",
                HttpContext.Request.Method, feature.Path, requestId);
        }

        Response.StatusCode = StatusCodes.Status500InternalServerError;
        if (feature?.Path?.StartsWith("/api", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Problem(title: "An unexpected error occurred.", statusCode: 500, detail: $"Request ID: {requestId}");
        }

        return View("Error", new ErrorViewModel { RequestId = requestId });
    }

    [Route("/error/{statusCode:int}")]
    [IgnoreAntiforgeryToken]
    public IActionResult Status(int statusCode)
    {
        var feature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        if (statusCode == 404)
        {
            logger.LogInformation("404 Not Found: {Path}", feature?.OriginalPath);
        }

        Response.StatusCode = statusCode;
        var model = statusCode switch
        {
            404 => new ErrorViewModel { StatusCode = 404, Title = "Page not found", Message = "The page or record you are looking for doesn't exist or has been removed." },
            400 => new ErrorViewModel { StatusCode = 400, Title = "Bad request", Message = "The request could not be processed. Please refresh the page and try again." },
            _ => new ErrorViewModel { StatusCode = statusCode, Title = $"Error {statusCode}", Message = "The request could not be completed." }
        };
        model.RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View("Error", model);
    }
}
