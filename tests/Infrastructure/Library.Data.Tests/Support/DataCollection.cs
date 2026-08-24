using Library.Testing.Containers;

namespace Library.Data.Tests.Support;

/// <summary>
///     xUnit collection sharing one <see cref="LibraryContainers" /> across every integration test
///     class, so PostgreSQL and Redis are started once for the suite rather than once per class.
/// </summary>
[CollectionDefinition(nameof(DataCollection))]
public sealed class DataCollection : ICollectionFixture<LibraryContainers>;
