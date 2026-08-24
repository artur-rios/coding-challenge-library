using Library.Shared.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Testing.Mock;

/// <summary>Builds a real <see cref="LibraryMetrics" /> for a test.</summary>
/// <remarks>
///     A real one rather than a mock: the counters are cheap, and constructing them here means a
///     rename or a signature change in <see cref="LibraryMetrics" /> breaks the tests at compile time
///     instead of leaving a mock happily verifying a method nobody calls any more.
/// </remarks>
public static class TestMetrics
{
    /// <summary>Creates metrics over a throwaway meter factory.</summary>
    public static LibraryMetrics Create() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider()
            .GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
}
