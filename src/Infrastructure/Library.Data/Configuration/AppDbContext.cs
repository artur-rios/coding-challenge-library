using Library.Data.EntityMaps;
using Library.Data.Relational.Configuration;
using Library.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Library.Data.Configuration;

/// <summary>The application's database context. One schema, five tables, no surprises.</summary>
public class AppDbContext(DbContextOptions options, DbContextDiagnosticsOptions diagnostics)
    : BaseDbContext(options)
{
    /// <summary>
    ///     The schema every table lives in.
    /// </summary>
    /// <remarks>
    ///     Named rather than left as <c>public</c> so this deployment owns its own namespace inside a
    ///     database it may share, and so a connection whose search path resolves <c>"$user"</c> to
    ///     something else cannot end up writing half the tables somewhere unexpected.
    /// </remarks>
    public const string Schema = "library";

    public DbSet<Book> Books { get; init; }
    public DbSet<User> Users { get; init; }
    public DbSet<Loan> Loans { get; init; }
    public DbSet<AuditEvent> AuditEvents { get; init; }
    public DbSet<IdempotencyRecord> IdempotencyRecords { get; init; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            // snake_case throughout, so the SQL somebody writes by hand against this database looks
            // like PostgreSQL rather than like C#.
            .UseSnakeCaseNamingConvention()
            .EnableDetailedErrors(diagnostics.DetailedErrors)
            .EnableSensitiveDataLogging(diagnostics.SensitiveDataLogging);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Book>().Configure();
        modelBuilder.Entity<User>().Configure();
        modelBuilder.Entity<Loan>().Configure();
        modelBuilder.Entity<AuditEvent>().Configure();
        modelBuilder.Entity<IdempotencyRecord>().Configure();
    }
}
