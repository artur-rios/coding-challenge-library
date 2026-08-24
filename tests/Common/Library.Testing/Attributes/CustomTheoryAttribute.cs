using Xunit;
using Xunit.Sdk;

namespace Library.Testing.Attributes;

/// <summary>
///     Base <see cref="TheoryAttribute" /> that stamps its <see cref="TestType" /> as the
///     <c>Category</c> trait, and can skip on an explicit condition.
/// </summary>
[TraitDiscoverer("Library.Testing.Attributes.TestTypeTraitDiscoverer", "Library.Testing")]
public class CustomTheoryAttribute : TheoryAttribute, ITraitAttribute
{
    /// <summary>Creates the attribute and applies the skip reason, if any.</summary>
    protected CustomTheoryAttribute(TestType testType, bool skipCondition = false)
    {
        TestType = testType;

        if (skipCondition)
        {
            Skip = "Condition to skip matched";
        }
    }

    /// <summary>The kind of test this attribute marks, published as the <c>Category</c> trait.</summary>
    public TestType TestType { get; }
}
