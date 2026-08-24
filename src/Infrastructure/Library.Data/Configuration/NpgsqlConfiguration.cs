using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Library.Data.Configuration;

/// <summary>
///     The provider settings the application and the EF Core tooling must agree on, stated once.
/// </summary>
/// <remarks>
///     <para>
///         The migrations history table is the reason this class exists. EF looks it up by the name
///         and schema configured on the context, so the runtime and design time have to name the same
///         table - and when they did not, the first container to start recorded its history in one
///         place and every run afterwards read an empty history from another, concluded that nothing
///         had ever been applied, and died trying to create tables that already existed.
///     </para>
///     <para>
///         It is qualified to <c>public</c> rather than to the application's own schema, because it
///         has to exist before the migration that creates that schema runs. Every table the model
///         owns is schema-qualified by the model itself, so nothing else depends on the connection's
///         search path.
///     </para>
/// </remarks>
public static class NpgsqlConfiguration
{
    /// <summary>The migrations history table's name.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    ///     The schema the history table lives in - <c>public</c>, which always exists, unlike the
    ///     application's schema at the moment the first migration is applied.
    /// </summary>
    public const string MigrationsHistorySchema = "public";

    /// <summary>Applies the settings shared by the host and the design-time tooling.</summary>
    public static void Apply(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(MigrationsHistoryTable, MigrationsHistorySchema);
}
