namespace Library.Testing.Mock;

/// <summary>
///     A <see cref="TimeProvider" /> that stands still, so a test can assert on a computed due date.
/// </summary>
/// <remarks>
///     Without it, "the loan is due fourteen days from now" can only be asserted with a tolerance, and
///     a rounding bug hides inside the tolerance. <see cref="Advance" /> moves it, for the overdue
///     case.
/// </remarks>
public class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan interval) => _now = _now.Add(interval);
}
