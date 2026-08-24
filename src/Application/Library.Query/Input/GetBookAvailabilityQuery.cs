using Library.Mediator.Query;

namespace Library.Query.Input;

/// <summary>Reads how many copies of one book are on the shelf.</summary>
public class GetBookAvailabilityQuery : BaseQuery
{
    /// <summary>The book's public identifier.</summary>
    public Guid BookId { get; set; }
}
