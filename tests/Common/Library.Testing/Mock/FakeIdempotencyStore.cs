using Library.Data.Relational.Repositories;
using Library.Shared.Idempotency;
using Npgsql;

namespace Library.Testing.Mock;

/// <summary>In-memory idempotency store, with the unique-key collision reproducible on demand.</summary>
public class FakeIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<string, StoredIdempotentResponse> _records = [];

    /// <summary>
    ///     When set, the next save throws a PostgreSQL unique violation - the collision that happens
    ///     when a concurrent request claimed the same key first.
    /// </summary>
    /// <remarks>
    ///     The exception has to be a real one carrying SQLSTATE <c>23505</c>, because the handler's
    ///     catch filter is <see cref="RelationalErrors.IsUniqueViolation" /> and testing against a
    ///     generic exception would prove nothing about that filter.
    /// </remarks>
    public bool NextSaveCollides { get; set; }

    /// <summary>Everything saved, for assertions.</summary>
    public IReadOnlyDictionary<string, StoredIdempotentResponse> Records => _records;

    public Task<StoredIdempotentResponse?> FindAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_records.GetValueOrDefault(key));

    public Task SaveAsync(StoredIdempotentResponse response, CancellationToken ct = default)
    {
        if (NextSaveCollides)
        {
            NextSaveCollides = false;

            throw UniqueViolation();
        }

        _records[response.Key] = response;

        return Task.CompletedTask;
    }

    /// <summary>Builds a Npgsql exception that <c>RelationalErrors</c> classifies as a duplicate key.</summary>
    public static PostgresException UniqueViolation() =>
        new(
            messageText: "duplicate key value violates unique constraint \"ux_idempotency_record_key\"",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: RelationalErrors.UniqueViolationSqlState);
}
