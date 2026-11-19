using Npgsql;

namespace TravelEase.Configuration;

/// <summary>
/// Builds the PostgreSQL connection string from configuration. Nothing is hardcoded.
///
/// Resolution order:
///   1. ConnectionStrings:DefaultConnection   (env: ConnectionStrings__DefaultConnection)
///   2. Individual variables DB_HOST, DB_PORT, DB_NAME, DB_USER
///
/// The password can be supplied separately (and is applied when the connection string has none):
///   DB_PASSWORD          plain environment variable
///   DB_PASSWORD_FILE     path to a file containing the password (Docker / Kubernetes secrets)
/// </summary>
public static class DatabaseConnectionResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        var builder = new NpgsqlConnectionStringBuilder();

        var configured = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            builder.ConnectionString = configured;
        }
        else
        {
            builder.Host = Value(configuration, "DB_HOST") ?? "localhost";
            builder.Port = int.TryParse(Value(configuration, "DB_PORT"), out var port) ? port : 5432;
            builder.Database = Value(configuration, "DB_NAME") ?? "travelease";
            builder.Username = Value(configuration, "DB_USER") ?? "travelease";
        }

        // Individual overrides always win over the connection string, which makes it easy to
        // point the same image at another server with a single variable (e.g. -e DB_HOST=db).
        if (Value(configuration, "DB_HOST") is { } host) builder.Host = host;
        if (int.TryParse(Value(configuration, "DB_PORT"), out var overridePort)) builder.Port = overridePort;
        if (Value(configuration, "DB_NAME") is { } database) builder.Database = database;
        if (Value(configuration, "DB_USER") is { } user) builder.Username = user;

        if (string.IsNullOrEmpty(builder.Password) && ReadPassword(configuration) is { } password)
        {
            builder.Password = password;
        }

        builder.ApplicationName ??= "TravelEase";
        if (builder.Timeout == 15) builder.Timeout = 5; // fail fast so health checks stay responsive

        return builder.ConnectionString;
    }

    /// <summary>Returns the connection string with the password masked, safe to display or log.</summary>
    public static string Mask(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Password)) builder.Password = "********";
        return builder.ConnectionString;
    }

    private static string? ReadPassword(IConfiguration configuration)
    {
        var file = Value(configuration, "DB_PASSWORD_FILE");
        if (file is not null && File.Exists(file))
        {
            return File.ReadAllText(file).Trim();
        }

        return Value(configuration, "DB_PASSWORD");
    }

    private static string? Value(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
