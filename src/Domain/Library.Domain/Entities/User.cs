using System.ComponentModel.DataAnnotations;
using Library.Data.Relational.Entities;

namespace Library.Domain.Entities;

/// <summary>A registered reader: whoever a loan can be made out to.</summary>
public class User : Entity
{
    /// <summary>External identifier, used everywhere the reader is addressed from outside the database.</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>Reader name. Required, max 200 characters.</summary>
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     E-mail address, stored lower-cased and unique. Unique because it is the one field a second
    ///     registration of the same person would collide on, and a duplicate reader would split their
    ///     loan history in two.
    /// </summary>
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Registration timestamp, UTC.</summary>
    public DateTime CreatedAt { get; set; }

    // Navigation properties

    /// <summary>Every loan ever made to this reader, in any state.</summary>
    public ICollection<Loan> Loans { get; set; } = [];
}
