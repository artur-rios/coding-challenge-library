namespace Library.Testing.Attributes;

/// <summary>Marks a unit test. Stamps the <c>Category=Unit</c> trait.</summary>
public class UnitFactAttribute(bool skipCondition = false)
    : CustomFactAttribute(TestType.Unit, skipCondition);

/// <summary>Marks a unit theory. Stamps the <c>Category=Unit</c> trait.</summary>
public class UnitTheoryAttribute(bool skipCondition = false)
    : CustomTheoryAttribute(TestType.Unit, skipCondition);

/// <summary>Marks an integration test - one that needs a real container. <c>Category=Integration</c>.</summary>
public class IntegrationFactAttribute(bool skipCondition = false)
    : CustomFactAttribute(TestType.Integration, skipCondition);

/// <summary>Marks an integration theory. <c>Category=Integration</c>.</summary>
public class IntegrationTheoryAttribute(bool skipCondition = false)
    : CustomTheoryAttribute(TestType.Integration, skipCondition);

/// <summary>Marks an end-to-end test through the HTTP pipeline. <c>Category=Functional</c>.</summary>
public class FunctionalFactAttribute(bool skipCondition = false)
    : CustomFactAttribute(TestType.Functional, skipCondition);

/// <summary>Marks an end-to-end theory. <c>Category=Functional</c>.</summary>
public class FunctionalTheoryAttribute(bool skipCondition = false)
    : CustomTheoryAttribute(TestType.Functional, skipCondition);
