using Library.Data.Idempotency;
using Library.Data.Relational.Repositories;
using Library.Data.Tests.Support;
using Library.Shared.Idempotency;
using Library.Testing.Attributes;
using Library.Testing.Containers;
using Microsoft.EntityFrameworkCore;

namespace Library.Data.Tests;

// Integration tests for the idempotency store against a real PostgreSQL. The point of running these
// against the database rather than a fake is the unique index: it is the thing that actually makes
// the guarantee hold when two replicas insert the same key at the same instant, and a fake can only
// pretend to have one.
[Collection(nameof(DataCollection))]
public class IdempotencyStoreTests(LibraryContainers containers)
{
    private static StoredIdempotentResponse Response(string key, string fingerprint = "abc") =>
        new(key, "POST /loans", fingerprint, 201, """{"id":"1"}""");

    [IntegrationFact]
    public async Task GivenAnUnseenKey_WhenLookingItUp_ThenNothingIsFound()
    {
        // Given
        await using var context = containers.CreateContext();
        var store = new IdempotencyStore(context, new IdempotencyOptions());

        // When / Then
        Assert.Null(await store.FindAsync(Guid.NewGuid().ToString()));
    }

    [IntegrationFact]
    public async Task GivenASavedKey_WhenLookingItUp_ThenTheStoredResponseComesBack()
    {
        // Given
        var key = Guid.NewGuid().ToString();

        await using var context = containers.CreateContext();
        var store = new IdempotencyStore(context, new IdempotencyOptions());

        await store.SaveAsync(Response(key));

        // When
        var found = await store.FindAsync(key);

        // Then - byte for byte, so a replay answers exactly what the first call did
        Assert.NotNull(found);
        Assert.Equal("POST /loans", found.Endpoint);
        Assert.Equal(201, found.StatusCode);
        Assert.Equal("""{"id":"1"}""", found.ResponseBody);
    }

    [IntegrationFact]
    public async Task GivenAKeyAlreadyTaken_WhenSavingItAgain_ThenTheDatabaseRaisesAUniqueViolation()
    {
        // Given a key one request has already recorded
        var key = Guid.NewGuid().ToString();

        await using var first = containers.CreateContext();
        await new IdempotencyStore(first, new IdempotencyOptions()).SaveAsync(Response(key));

        // When a second request - a second replica, in production - records the same key
        await using var second = containers.CreateContext();
        var store = new IdempotencyStore(second, new IdempotencyOptions());

        var exception = await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => store.SaveAsync(Response(key)));

        // Then - it surfaces as a classifiable unique violation rather than being swallowed, which is
        // what lets the loan handler roll its own transaction back instead of committing a second loan
        Assert.True(RelationalErrors.IsUniqueViolation(exception));
    }

    [IntegrationFact]
    public async Task GivenManyConcurrentSavesOfOneKey_WhenRacing_ThenExactlyOneSucceeds()
    {
        // Given one key and eight replicas trying to claim it at once
        var key = Guid.NewGuid().ToString();

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = containers.CreateContext();

            try
            {
                await new IdempotencyStore(context, new IdempotencyOptions()).SaveAsync(Response(key));

                return true;
            }
            catch (Exception exception) when (RelationalErrors.IsUniqueViolation(exception))
            {
                return false;
            }
        });

        var results = await Task.WhenAll(attempts);

        // Then - the index, not the lookup, is the guarantee
        Assert.Equal(1, results.Count(succeeded => succeeded));

        await using var verification = containers.CreateContext();

        Assert.Equal(1, await verification.IdempotencyRecords.CountAsync(record => record.Key == key));
    }

    [IntegrationFact]
    public async Task GivenAnExpiredKey_WhenLookingItUp_ThenItIsTreatedAsAbsent()
    {
        // Given a key saved with a retention that has already elapsed
        var key = Guid.NewGuid().ToString();

        await using var context = containers.CreateContext();

        var store = new IdempotencyStore(
            context, new IdempotencyOptions { Retention = TimeSpan.FromMilliseconds(-1) });

        await store.SaveAsync(Response(key));

        // When
        var found = await store.FindAsync(key);

        // Then - a caller retrying a day later is making a new request, not finishing an old one
        Assert.Null(found);
    }

    [IntegrationFact]
    public async Task GivenASavedKey_WhenSavingIt_ThenItsExpiryFollowsTheConfiguredRetention()
    {
        // Given
        var key = Guid.NewGuid().ToString();

        await using var context = containers.CreateContext();

        var store = new IdempotencyStore(context, new IdempotencyOptions { Retention = TimeSpan.FromHours(6) });

        await store.SaveAsync(Response(key));

        // When
        await using var verification = containers.CreateContext();

        var record = await verification.IdempotencyRecords.FirstAsync(entity => entity.Key == key);

        // Then - within a minute of six hours out, which is generous enough not to be flaky and tight
        // enough to catch a retention that was ignored
        Assert.InRange(
            record.ExpiresAt - record.CreatedAt,
            TimeSpan.FromHours(6) - TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1));
    }
}
