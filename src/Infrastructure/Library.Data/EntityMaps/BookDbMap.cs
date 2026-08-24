using Library.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Library.Data.EntityMaps;

internal static class BookDbMap
{
    public static void Configure(this EntityTypeBuilder<Book> book)
    {
        book.ToTable("book", table =>
        {
            // The invariants, restated where they cannot be bypassed. The application maintains these
            // counts through conditional updates that already enforce them; these constraints are what
            // guarantees they hold even if a migration, a fix-up script or a future code path gets it
            // wrong. A negative available count is exactly the corruption the challenge asks to be
            // impossible, so it is worth saying twice.
            table.HasCheckConstraint("ck_book_total_copies_non_negative", "total_copies >= 0");
            table.HasCheckConstraint("ck_book_available_copies_non_negative", "available_copies >= 0");
            table.HasCheckConstraint(
                "ck_book_available_within_total", "available_copies <= total_copies");
        });

        book.HasKey(entity => entity.Id);

        book.Property(entity => entity.PublicId).IsRequired();
        book.HasIndex(entity => entity.PublicId).IsUnique();

        book.Property(entity => entity.Title).IsRequired().HasMaxLength(300);
        book.Property(entity => entity.Author).IsRequired().HasMaxLength(200);

        book.Property(entity => entity.Isbn).IsRequired().HasMaxLength(13);
        // The uniqueness the catalogue actually needs. It is on the normalised value, which is why
        // the handlers normalise before writing.
        book.HasIndex(entity => entity.Isbn).IsUnique().HasDatabaseName("ux_book_isbn");

        book.Property(entity => entity.TotalCopies).IsRequired();
        book.Property(entity => entity.AvailableCopies).IsRequired();
        book.Property(entity => entity.IsActive).IsRequired().HasDefaultValue(true);

        book.Property(entity => entity.ConcurrencyStamp).IsRequired().IsConcurrencyToken();

        book.Property(entity => entity.CreatedAt).IsRequired();
        book.Property(entity => entity.UpdatedAt).IsRequired();

        // No index on title or author is declared here, and that is deliberate rather than an
        // oversight. The listing filters case-insensitively, which PostgreSQL renders as
        // LOWER(title) LIKE '%...%' - a plain B-tree index on the column cannot serve that, so
        // declaring one would look like coverage while providing none. The migration creates GIN
        // trigram indexes on LOWER(title) and LOWER(author) instead, which do; EF Core cannot express
        // them, so they live in the migration as SQL.
    }
}
