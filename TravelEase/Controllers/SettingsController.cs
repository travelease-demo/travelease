using Microsoft.AspNetCore.Mvc;
using TravelEase.Services;

namespace TravelEase.Controllers;

public class SettingsController(ISystemInfoService systemInfo) : AppController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Settings";
        return View(await systemInfo.GetAsync(ct));
    }
}
