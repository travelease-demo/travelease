namespace TravelEase.Models;

/// <summary>Outcome of a business operation. Keeps controllers free of exception-driven flow.</summary>
public class ServiceResult
{
    public bool Succeeded { get; protected init; }
    public string? Error { get; protected init; }

    /// <summary>Optional name of the form field the error relates to.</summary>
    public string? Field { get; protected init; }

    public bool NotFound { get; protected init; }

    public static ServiceResult Ok() => new() { Succeeded = true };
    public static ServiceResult Fail(string error, string? field = null) => new() { Error = error, Field = field };
    public static ServiceResult Missing(string error = "The requested record was not found.") => new() { Error = error, NotFound = true };
}

public sealed class ServiceResult<T> : ServiceResult
{
    public T? Value { get; private init; }

    public static ServiceResult<T> Ok(T value) => new() { Succeeded = true, Value = value };
    public new static ServiceResult<T> Fail(string error, string? field = null) => new() { Error = error, Field = field };
    public new static ServiceResult<T> Missing(string error = "The requested record was not found.") => new() { Error = error, NotFound = true };
}
