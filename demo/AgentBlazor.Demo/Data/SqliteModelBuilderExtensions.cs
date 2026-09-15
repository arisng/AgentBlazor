using AgentBlazor.Core.Persistence;
using AgentBlazor.Demo.Data.ValueGeneration;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// SQLite-specific model builder extensions for TPC identity generation.
/// Only needed when using SQLite with TPC mapping strategy — not needed for
/// SQL Server or PostgreSQL which support server-side identity generation.
/// </summary>
internal static class SqliteModelBuilderExtensions
{
    /// <summary>
    /// Configures client-side Guid identity generation for a concrete entity type
    /// in a TPC hierarchy on SQLite.
    /// </summary>
    internal static void ConfigureSqliteIdentity<T>(this ModelBuilder modelBuilder)
        where T : class
    {
        modelBuilder.Entity<T>()
            .Property<Guid>("Id")
            .HasValueGeneratorFactory<SqliteTpcGuidValueGeneratorFactory>();
    }
}
