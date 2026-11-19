using Microsoft.AspNetCore.Mvc;
using TravelEase.Services;

namespace TravelEase.Controllers;

public class DashboardController(IDashboardService dashboard) : AppController
{
    [HttpGet("/")]
    [HttpGet("/dashboard")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Dashboard";
        return View(await dashboard.GetDashboardAsync(ct));
    }
}
