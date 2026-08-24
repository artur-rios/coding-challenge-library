namespace Library.Data.Configuration;

/// <summary>
///     Whether the context logs parameter values and detailed errors.
/// </summary>
/// <remarks>
///     A record rather than a pair of booleans read from configuration at the call site, because these
///     two are off in production for a reason and that reason should be visible where they are set:
///     sensitive-data logging writes every parameter value into the log, which for this API means
///     reader names and e-mail addresses on every query.
/// </remarks>
public record DbContextDiagnosticsOptions
{
    /// <summary>Whether EF Core includes parameter values in its logs.</summary>
    public bool SensitiveDataLogging { get; init; }

    /// <summary>Whether EF Core includes detailed exception information.</summary>
    public bool DetailedErrors { get; init; }

    /// <summary>Both off - the production setting, and the one design-time tooling uses.</summary>
    public static DbContextDiagnosticsOptions Disabled => new();
}
