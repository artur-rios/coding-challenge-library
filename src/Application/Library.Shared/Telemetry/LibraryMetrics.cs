using System.Diagnostics.Metrics;

namespace Library.Shared.Telemetry;

/// <summary>
///     The business metrics the challenge asks for: loans created, loans refused because nothing was
///     available, idempotent repeats answered from the store, and the latency of the lending endpoint.
/// </summary>
/// <remarks>
///     <para>
///         Built on <see cref="Meter" /> from the BCL rather than on an OpenTelemetry type, so the
///         application layer stays free of the exporter it happens to be wired to. The web layer
///         subscribes this meter by name; swapping OTLP for anything else changes that registration
///         and nothing here.
///     </para>
///     <para>
///         These four are not arbitrary. Together they answer the question an operator actually has
///         during an incident: are loans still being made, is the refusal rate normal or has something
///         made every book look unavailable, are clients retrying, and is the endpoint slow. The
///         refusal counter in particular is the one that distinguishes "the library ran out of copies"
///         from "the API is broken", which no HTTP-level metric can tell apart - both are 409s.
///     </para>
/// </remarks>
public sealed class LibraryMetrics : IDisposable
{
    /// <summary>The meter name the telemetry pipeline subscribes to.</summary>
    public const string MeterName = "Library.Loans";

    private readonly Meter _meter;
    private readonly Counter<long> _loansCreated;
    private readonly Counter<long> _loansRejectedUnavailable;
    private readonly Counter<long> _idempotentReplays;
    private readonly Histogram<double> _loanEndpointDuration;

    public LibraryMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _loansCreated = _meter.CreateCounter<long>(
            "library.loans.created", "{loan}", "Loans successfully created.");

        _loansRejectedUnavailable = _meter.CreateCounter<long>(
            "library.loans.rejected_unavailable", "{loan}",
            "Loan attempts refused because no copy was available.");

        _idempotentReplays = _meter.CreateCounter<long>(
            "library.loans.idempotent_replays", "{request}",
            "Repeated loan requests answered from the idempotency store instead of executed.");

        _loanEndpointDuration = _meter.CreateHistogram<double>(
            "library.loans.request.duration", "ms", "Latency of the loan creation endpoint.");
    }

    /// <summary>Records that a loan was created.</summary>
    public void LoanCreated() => _loansCreated.Add(1);

    /// <summary>Records that a loan was refused for want of an available copy.</summary>
    public void LoanRejectedUnavailable() => _loansRejectedUnavailable.Add(1);

    /// <summary>Records that a repeated request was answered from the idempotency store.</summary>
    public void IdempotentReplay() => _idempotentReplays.Add(1);

    /// <summary>Records the latency of one call to the loan creation endpoint.</summary>
    /// <param name="outcome">
    ///     How the call ended - <c>created</c>, <c>replayed</c> or <c>rejected</c> - so the histogram
    ///     can be split by outcome. A refusal is usually faster than a creation, and mixing them hides
    ///     a regression in the path that matters.
    /// </param>
    public void RecordLoanRequestDuration(double milliseconds, string outcome) =>
        _loanEndpointDuration.Record(milliseconds, new KeyValuePair<string, object?>("outcome", outcome));

    public void Dispose() => _meter.Dispose();
}
