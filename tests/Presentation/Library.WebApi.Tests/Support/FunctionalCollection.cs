using Library.Testing.Containers;

namespace Library.WebApi.Tests.Support;

/// <summary>
///     xUnit collection sharing one <see cref="LibraryContainers" /> across every functional test
///     class, so PostgreSQL and Redis start once for the suite.
/// </summary>
/// <remarks>
///     Also serialises the classes, which matters here: the containers publish their connection
///     strings through environment variables, and the host under test reads them at start-up. Two
///     classes booting hosts in parallel against different variables would be a race with no useful
///     outcome.
/// </remarks>
[CollectionDefinition(nameof(FunctionalCollection))]
public sealed class FunctionalCollection : ICollectionFixture<LibraryContainers>;
