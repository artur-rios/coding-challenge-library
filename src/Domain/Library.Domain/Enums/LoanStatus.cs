using System.ComponentModel;

namespace Library.Domain.Enums;

/// <summary>
///     The states a loan moves through. A loan never leaves the table: returning and cancelling are
///     both state changes, so the fact that the loan existed - and who held the copy, and when -
///     survives either one.
/// </summary>
public enum LoanStatus
{
    /// <summary>The copy is out. This is the only state that holds a copy against availability.</summary>
    [Description("The copy is currently with the reader")]
    Active = 1,

    /// <summary>The copy came back. Terminal.</summary>
    [Description("The copy was returned to the library")]
    Returned = 2,

    /// <summary>
    ///     The loan was called off. Terminal, and deliberately distinct from
    ///     <see cref="Returned" />: both release the copy, but only one of them means the reader ever
    ///     had it.
    /// </summary>
    [Description("The loan was cancelled before or instead of a return")]
    Cancelled = 3
}
