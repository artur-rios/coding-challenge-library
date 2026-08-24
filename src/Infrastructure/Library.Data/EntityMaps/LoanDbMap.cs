using Library.Domain.Entities;
using Library.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Library.Data.EntityMaps;

internal static class LoanDbMap
{
    public static void Configure(this EntityTypeBuilder<Loan> loan)
    {
        loan.ToTable("loan", table =>
            // The state machine, restated in SQL: a returned loan has a return timestamp, a cancelled
            // one has a cancellation timestamp, and an active one has neither. Without this, a bug in
            // a transition could leave a row that claims to be returned with no record of when - and
            // the history endpoints would report it without noticing.
            table.HasCheckConstraint(
                "ck_loan_status_timestamps",
                """
                (status = 1 AND returned_at IS NULL AND cancelled_at IS NULL)
                OR (status = 2 AND returned_at IS NOT NULL AND cancelled_at IS NULL)
                OR (status = 3 AND cancelled_at IS NOT NULL AND returned_at IS NULL)
                """));

        loan.HasKey(entity => entity.Id);

        loan.Property(entity => entity.PublicId).IsRequired();
        loan.HasIndex(entity => entity.PublicId).IsUnique();

        // Stored as the underlying int rather than as a string. The numbers are part of the check
        // constraint above and of the conditional updates, so they are already a contract with the
        // database; writing them as text would mean two spellings of the same fact.
        loan.Property(entity => entity.Status).IsRequired().HasConversion<int>();

        loan.Property(entity => entity.LoanedAt).IsRequired();
        loan.Property(entity => entity.DueAt).IsRequired();
        loan.Property(entity => entity.ReturnedAt);
        loan.Property(entity => entity.CancelledAt);
        loan.Property(entity => entity.IdempotencyKey).HasMaxLength(200);

        // Restrict, not cascade, and this is the point of the whole "history is not deleted"
        // requirement: deleting a book with loans is refused by the database itself, so no code path -
        // present or future - can quietly take the history with it.
        loan.HasOne(entity => entity.Book)
            .WithMany(book => book.Loans)
            .HasForeignKey(entity => entity.BookId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        loan.HasOne(entity => entity.User)
            .WithMany(user => user.Loans)
            .HasForeignKey(entity => entity.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // A reader may hold one copy of a title at a time. Partial, so the rule applies to active
        // loans only: the same reader borrowing the same book again next year is ordinary, and a full
        // unique index would forbid it.
        loan.HasIndex(entity => new { entity.BookId, entity.UserId })
            .IsUnique()
            .HasFilter($"status = {(int)LoanStatus.Active}")
            .HasDatabaseName("ux_loan_active_book_user");

        // The history endpoints' access paths: every loan of a book, newest first, and the same by
        // reader.
        loan.HasIndex(entity => new { entity.BookId, entity.LoanedAt })
            .HasDatabaseName("ix_loan_book_loaned_at");

        loan.HasIndex(entity => new { entity.UserId, entity.LoanedAt })
            .HasDatabaseName("ix_loan_user_loaned_at");
    }
}
