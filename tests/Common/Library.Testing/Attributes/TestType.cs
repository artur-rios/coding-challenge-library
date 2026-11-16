namespace Library.Testing.Attributes;

/// <summary>
///     The kinds of test in this repository, published as the <c>Category</c> trait so CI can run
///     them separately.
/// </summary>
/// <remarks>
///     The split exists because the suites cost different things. Unit tests need nothing and run in
///     seconds; the other two start containers, so on a machine without Docker the unit suite runs on
///     its own. CI runs all three in one pass: the unit projects finish while the others start their
///     containers, rather than every project's process starting once per suite.
/// </remarks>
public enum TestType
{
    /// <summary>No I/O. Business rules against fakes.</summary>
    Unit,

    /// <summary>Against a real PostgreSQL, or Redis, in a container - but not through the API.</summary>
    Integration,

    /// <summary>End to end, through the HTTP pipeline, against real containers.</summary>
    Functional
}
