namespace TravelEase.Infrastructure;

public static class HttpExtensions
{
    /// <summary>True for fetch/XHR requests issued by site.js (modal forms).</summary>
    public static bool IsAjax(this HttpRequest request) =>
        string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
