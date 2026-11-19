using System.Reflection;
using TravelEase.Configuration;

namespace TravelEase.Infrastructure;

public static class AppVersion
{
    private static readonly string AssemblyVersion =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>App:Version (env App__Version) wins; otherwise the version compiled into the assembly.</summary>
    public static string Resolve(AppOptions options) =>
        string.IsNullOrWhiteSpace(options.Version) ? AssemblyVersion : options.Version.Trim();
}
