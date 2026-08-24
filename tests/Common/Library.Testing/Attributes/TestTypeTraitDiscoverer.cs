using Xunit.Abstractions;
using Xunit.Sdk;

namespace Library.Testing.Attributes;

/// <summary>
///     Publishes a test attribute's <see cref="TestType" /> as the <c>Category</c> trait, so
///     <c>dotnet test --filter "Category=Unit"</c> works without any per-project configuration.
/// </summary>
public class TestTypeTraitDiscoverer : ITraitDiscoverer
{
    /// <summary>The trait key the suites are filtered on.</summary>
    public const string CategoryKey = "Category";

    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        yield return new KeyValuePair<string, string>(
            CategoryKey, traitAttribute.GetNamedArgument<TestType>(nameof(CustomFactAttribute.TestType)).ToString());
    }
}
