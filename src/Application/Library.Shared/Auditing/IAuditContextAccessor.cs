namespace Library.Shared.Auditing;

/// <summary>
///     Supplies the two things an audit entry needs from the request it belongs to but that no
///     command carries: who is acting, and under which correlation identifier.
/// </summary>
/// <remarks>
///     An interface in the application layer with its implementation in the web layer, so a handler
///     can be unit-tested without an <c>HttpContext</c> and so the audit writer never reaches into
///     ASP.NET Core types.
/// </remarks>
public interface IAuditContextAccessor
{
    /// <summary>
    ///     Who is performing the operation, from the <c>X-Actor</c> request header, or
    ///     <c>"anonymous"</c> when the caller named nobody.
    /// </summary>
    string Actor { get; }

    /// <summary>The correlation identifier of the current request.</summary>
    string CorrelationId { get; }
}
