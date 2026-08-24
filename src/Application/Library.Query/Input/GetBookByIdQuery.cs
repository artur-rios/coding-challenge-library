using Library.Mediator.Query;

namespace Library.Query.Input;

/// <summary>Reads one book by its public identifier.</summary>
public class GetBookByIdQuery : BaseQuery
{
    /// <summary>The book's public identifier.</summary>
    public Guid Id { get; set; }
}
