namespace TravelEase.Configuration;

/// <summary>General application settings (section "App"). Override with App__Name, App__Version, ...</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    public string Name { get; set; } = "TravelEase";
    public string Tagline { get; set; } = "Travel Booking Management";

    /// <summary>When empty, the assembly informational version (set by -p:Version=...) is used.</summary>
    public string Version { get; set; } = string.Empty;

    public string CurrencySymbol { get; set; } = "₹";

    /// <summary>Culture used for number formatting. Falls back to invariant when ICU is unavailable.</summary>
    public string Culture { get; set; } = "en-IN";
}

/// <summary>Database start-up behaviour (section "Database").</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool ApplyMigrationsOnStartup { get; set; } = true;
    public bool SeedSampleData { get; set; } = true;

    /// <summary>0 = retry forever (useful when the database container starts after the app).</summary>
    public int MaxStartupRetries { get; set; }

    public int RetryDelaySeconds { get; set; } = 5;
    public int CommandTimeoutSeconds { get; set; } = 30;
}

/// <summary>File system locations (section "Storage"). Point these at Docker volumes.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Directory where generated CSV reports are written. Relative paths resolve against the content root.</summary>
    public string ReportsPath { get; set; } = "data/reports";

    /// <summary>Directory for ASP.NET Core Data Protection keys (antiforgery tokens survive container restarts).</summary>
    public string DataProtectionKeysPath { get; set; } = "data/keys";
}
