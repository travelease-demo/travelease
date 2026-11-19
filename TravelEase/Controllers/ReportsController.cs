using Microsoft.AspNetCore.Mvc;
using TravelEase.Services;

namespace TravelEase.Controllers;

public class ReportsController(IReportService reports, IReportFileService files) : AppController
{
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, CancellationToken ct)
    {
        ViewData["Title"] = "Reports";
        return View(await reports.GetReportsAsync(from, to, ct));
    }

    /// <summary>Generates a CSV file in the reports directory (a Docker volume when containerised).</summary>
    [HttpPost]
    public async Task<IActionResult> Export(ReportType type, DateTime? from, DateTime? to, CancellationToken ct)
    {
        try
        {
            var data = await reports.GetReportsAsync(from, to, ct);
            var fileName = await files.ExportAsync(type, data, ct);
            Toast($"Report saved to {fileName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toast($"Could not write the report file: {ex.Message}", "error");
        }

        return RedirectToAction(nameof(Index), new { from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd") });
    }

    [HttpGet]
    public IActionResult Download(string file)
    {
        var path = files.GetFilePath(file);
        if (path is null) return NotFound();
        var contentType = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? "text/csv" : "application/octet-stream";
        return PhysicalFile(path, contentType, Path.GetFileName(path));
    }

    [HttpPost]
    public IActionResult DeleteFile(string file)
    {
        Toast(files.Delete(file) ? $"{file} deleted." : "File not found.", "success");
        return RedirectToAction(nameof(Index));
    }
}
