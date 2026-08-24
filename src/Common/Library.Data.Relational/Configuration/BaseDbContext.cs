using Library.Data.Relational.Entities;
using Microsoft.EntityFrameworkCore;

namespace Library.Data.Relational.Configuration;

/// <summary>
///     Base <see cref="DbContext" /> that refreshes the optimistic-concurrency stamp of modified
///     <see cref="VersionedEntity" /> instances on save.
/// </summary>
/// <param name="options">The context options supplied by the configured provider.</param>
public abstract class BaseDbContext(DbContextOptions options) : DbContext(options)
{
    /// <inheritdoc />
    public override int SaveChanges()
    {
        BumpConcurrencyStamps();

        return base.SaveChanges();
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BumpConcurrencyStamps();

        return base.SaveChangesAsync(cancellationToken);
    }

    private void BumpConcurrencyStamps()
    {
        foreach (var entry in ChangeTracker.Entries<VersionedEntity>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            entry.Entity.ConcurrencyStamp = Guid.NewGuid();
        }
    }
}
