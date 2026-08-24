namespace Library.Data.Idempotency;

/// <summary>How long a used idempotency key is remembered.</summary>
public record IdempotencyOptions
{
    /// <summary>Environment variable overriding <see cref="Retention" />, in hours.</summary>
    public const string RetentionHoursVariable = "LIBRARY_IDEMPOTENCY_RETENTION_HOURS";

    /// <summary>The default: long enough to cover any realistic retry, short enough not to accumulate.</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(24);

    /// <summary>
    ///     How long a key is honoured. After this a repeat of the same key does the work again, which
    ///     is the right answer: a client retrying a day later is making a new request, not finishing
    ///     an old one.
    /// </summary>
    public TimeSpan Retention { get; init; } = DefaultRetention;

    /// <summary>Reads the retention from the environment, falling back to the default.</summary>
    public static IdempotencyOptions FromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable(RetentionHoursVariable);

        return double.TryParse(configured, out var hours) && hours > 0
            ? new IdempotencyOptions { Retention = TimeSpan.FromHours(hours) }
            : new IdempotencyOptions();
    }
}
