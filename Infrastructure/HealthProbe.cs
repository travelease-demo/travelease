namespace TravelEase.Infrastructure;

/// <summary>
/// Minimal HTTP probe so a container can check its own health without curl/wget installed:
///
///   dotnet TravelEase.dll healthcheck                     -> GET http://localhost:{port}/health/live
///   dotnet TravelEase.dll healthcheck /health/ready       -> custom path
///
/// Exit code 0 = healthy, 1 = unhealthy (the contract Docker HEALTHCHECK expects).
/// The port comes from HEALTHCHECK_PORT, then ASPNETCORE_HTTP_PORTS, then 8080.
/// </summary>
public static class HealthProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "/health/live";
        var port = FirstPort(Environment.GetEnvironmentVariable("HEALTHCHECK_PORT"))
                   ?? FirstPort(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"))
                   ?? "8080";
        var url = $"http://127.0.0.1:{port}{(path.StartsWith('/') ? path : "/" + path)}";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await http.GetAsync(url);
            Console.WriteLine($"{(int)response.StatusCode} {url}");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Health probe failed for {url}: {ex.Message}");
            return 1;
        }
    }

    private static string? FirstPort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var first = value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return int.TryParse(first, out _) ? first : null;
    }
}
