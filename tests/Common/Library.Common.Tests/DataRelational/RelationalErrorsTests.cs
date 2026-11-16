using System.Data.Common;
using Library.Data.Relational.Repositories;
using Library.Testing.Attributes;
using Microsoft.EntityFrameworkCore;

namespace Library.Common.Tests.DataRelational;

// Unit tests for RelationalErrors: how a provider exception, however deeply wrapped, is classified
// and described. PostgreSQL's exception is a DbException with a SQLSTATE, so a stand-in with the
// same two properties exercises every branch without a database.
public class RelationalErrorsTests
{
    private sealed class FakeDbException(string? sqlState, string message = "error", bool isTransient = false)
        : DbException(message)
    {
        public override string? SqlState { get; } = sqlState;

        public override bool IsTransient { get; } = isTransient;
    }

    private static DbUpdateException Wrapped(Exception inner) => new("Saving failed.", inner);

    [UnitTheory]
    [InlineData("23505", true)]
    [InlineData("23503", false)]
    [InlineData("40001", false)]
    [InlineData(null, false)]
    public void GivenAWrappedProviderError_WhenCheckedForAUniqueViolation_ThenOnlyItsSqlStateDecides(
        string? sqlState, bool expected)
    {
        // When
        var isUnique = RelationalErrors.IsUniqueViolation(Wrapped(new FakeDbException(sqlState)));

        // Then
        Assert.Equal(expected, isUnique);
    }

    [UnitTheory]
    [InlineData("40001", true)]
    [InlineData("40P01", true)]
    [InlineData("23505", false)]
    [InlineData(null, false)]
    public void GivenAWrappedProviderError_WhenCheckedForARetryableConflict_ThenSerializationFailuresAndDeadlocksAre(
        string? sqlState, bool expected)
    {
        // When
        var retryable = RelationalErrors.IsRetryableConflict(Wrapped(new InvalidOperationException("outer", new FakeDbException(sqlState))));

        // Then
        Assert.Equal(expected, retryable);
    }

    [UnitFact]
    public void GivenAnExceptionWithNoProviderError_WhenChecked_ThenItIsNeitherUniqueNorRetryable()
    {
        // Given
        var exception = new InvalidOperationException("outer", new TimeoutException());

        // Then
        Assert.False(RelationalErrors.IsUniqueViolation(exception));
        Assert.False(RelationalErrors.IsRetryableConflict(exception));
    }

    [UnitFact]
    public void GivenAConcurrencyException_WhenDescribed_ThenItIsAConcurrencyConflict()
    {
        // When
        var description = RelationalErrors.Describe(new DbUpdateConcurrencyException("stale"));

        // Then
        Assert.Equal(RelationalErrors.ConcurrencyMessage, description);
    }

    [UnitFact]
    public void GivenATimeoutAnywhereInTheChain_WhenDescribed_ThenItIsTransient()
    {
        // When
        var description = RelationalErrors.Describe(Wrapped(new InvalidOperationException("middle", new TimeoutException())));

        // Then
        Assert.Equal(RelationalErrors.TransientMessage, description);
    }

    [UnitTheory]
    [InlineData("23505", RelationalErrors.UniqueViolationMessage)]
    [InlineData("40001", RelationalErrors.TransientMessage)]
    [InlineData("40P01", RelationalErrors.TransientMessage)]
    [InlineData("23503", RelationalErrors.IntegrityViolationMessage)]
    [InlineData("23502", RelationalErrors.IntegrityViolationMessage)]
    [InlineData("23514", RelationalErrors.IntegrityViolationMessage)]
    [InlineData("42P01", RelationalErrors.GenericMessage)]
    [InlineData(null, RelationalErrors.GenericMessage)]
    public void GivenAProviderErrorWithASqlState_WhenDescribed_ThenItsClassDecidesTheMessage(string? sqlState, string expected)
    {
        // When
        var description = RelationalErrors.Describe(Wrapped(new FakeDbException(sqlState)));

        // Then
        Assert.Equal(expected, description);
    }

    [UnitFact]
    public void GivenATransientProviderError_WhenDescribed_ThenItIsTransient()
    {
        // When
        var description = RelationalErrors.Describe(Wrapped(new FakeDbException("08006", isTransient: true)));

        // Then
        Assert.Equal(RelationalErrors.TransientMessage, description);
    }

    [UnitTheory]
    [InlineData("insert or update violates FOREIGN KEY constraint \"fk_loans_books\"")]
    [InlineData("new row violates check constraint \"ck_books_copies\"")]
    [InlineData("not-null constraint failed")]
    [InlineData("null value in column \"title\" violates")]
    public void GivenAProviderErrorWithoutASqlStateButAnIntegrityMessage_WhenDescribed_ThenItIsAnIntegrityViolation(
        string message)
    {
        // When
        var description = RelationalErrors.Describe(Wrapped(new FakeDbException(null, message)));

        // Then
        Assert.Equal(RelationalErrors.IntegrityViolationMessage, description);
    }

    [UnitFact]
    public void GivenAnExceptionWithNoProviderError_WhenDescribed_ThenItIsGeneric()
    {
        // When
        var description = RelationalErrors.Describe(new InvalidOperationException("table books does not exist"));

        // Then
        Assert.Equal(RelationalErrors.GenericMessage, description);
    }
}
