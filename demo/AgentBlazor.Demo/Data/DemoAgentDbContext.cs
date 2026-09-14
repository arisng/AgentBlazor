using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// SQLite-backed DbContext for persisted agent definitions — the store backing the
/// demo's database-backed <c>IAgentRegistry</c> (Agent Builder showcase). Registered
/// as an <see cref="IDbContextFactory{T}"/> so the singleton registry never captures
/// a scoped context.
/// </summary>
public sealed class DemoAgentDbContext(DbContextOptions<DemoAgentDbContext> options)
    : DbContext(options)
{
    public DbSet<AgentDefinitionEntity> AgentDefinitions => Set<AgentDefinitionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var agent = modelBuilder.Entity<AgentDefinitionEntity>();
        agent.ToTable("demo_agent_definitions");
        agent.HasKey(static x => x.Id);
        // Name is the case-insensitive lookup key the runtime resolves by.
        agent.HasIndex(static x => x.Name).IsUnique();
        agent.HasIndex(static x => x.TenantId);
        agent.Property(static x => x.Name).IsRequired().HasMaxLength(256);
        agent.Property(static x => x.Description).HasMaxLength(512);
        agent.Property(static x => x.AllowedComponentsJson).HasColumnType("text");
        agent.Property(static x => x.AllowedActionsJson).HasColumnType("text");
        agent.Property(static x => x.AllowedDataSchemasJson).HasColumnType("text");
        agent.Property(static x => x.MetadataJson).HasColumnType("text");
        agent.Property(static x => x.Persona).HasColumnType("text");
        agent.Property(static x => x.EnabledToolsJson).HasColumnType("text");
        agent.Property(static x => x.TenantId).HasMaxLength(128);
        agent.Property(static x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        agent.Property(static x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}