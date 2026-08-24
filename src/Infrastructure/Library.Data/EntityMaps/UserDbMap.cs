using Library.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Library.Data.EntityMaps;

internal static class UserDbMap
{
    public static void Configure(this EntityTypeBuilder<User> user)
    {
        user.ToTable("library_user");

        user.HasKey(entity => entity.Id);

        user.Property(entity => entity.PublicId).IsRequired();
        user.HasIndex(entity => entity.PublicId).IsUnique();

        user.Property(entity => entity.Name).IsRequired().HasMaxLength(200);

        user.Property(entity => entity.Email).IsRequired().HasMaxLength(320);
        // On the lower-cased value the handler writes, so this means "already registered" rather than
        // "identical string".
        user.HasIndex(entity => entity.Email).IsUnique().HasDatabaseName("ux_library_user_email");

        user.Property(entity => entity.CreatedAt).IsRequired();
    }
}
