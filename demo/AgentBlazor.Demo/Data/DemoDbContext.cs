using AgentBlazor.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// Unified SQLite-backed DbContext for all Demo persistence: conversation sessions,
/// turns, and agent definitions. Replaces the previous split
/// <c>DemoConversationDbContext</c> + <c>DemoAgentDbContext</c> with a single context
/// and code-first migrations.
/// </summary>
/// <remarks>
/// Registered as <c>IDbContextFactory&lt;DemoDbContext&gt;</c> so singleton services
/// never capture a scoped context. Schema is managed by EF Core migrations — no
/// hand-rolled <c>EnsureCreatedAsync</c> or additive column hacks.
/// </remarks>
public sealed class DemoDbContext(DbContextOptions<DemoDbContext> options)
    : DbContext(options)
{
    public DbSet<DemoConversationSessionEntity> Sessions => Set<DemoConversationSessionEntity>();
    public DbSet<DemoConversationTurnEntity> Turns => Set<DemoConversationTurnEntity>();
    public DbSet<DemoAgentDefinitionEntity> AgentDefinitions => Set<DemoAgentDefinitionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TPC (table-per-concrete-type) on abstract roots only — per official docs.
        // Convention handles: int PK AUTOINCREMENT, FK discovery (SessionId ↔ Session nav),
        // and navigation pairing. No explicit relationship config needed.
        modelBuilder.Entity<ConversationSessionEntity>().UseTpcMappingStrategy();
        modelBuilder.Entity<ConversationTurnEntity>().UseTpcMappingStrategy();
        modelBuilder.Entity<AgentDefinitionEntity>().UseTpcMappingStrategy();

        // ── Conversation Sessions ───────────────────────────────────────
        // SQLite+TPC workaround: client-side int Id generation.
        // For SQL Server/PostgreSQL, use standard UseAutoincrement() instead.
        modelBuilder.ConfigureSqliteIdentity<DemoConversationSessionEntity>();
        var session = modelBuilder.Entity<DemoConversationSessionEntity>();
        session.ToTable("demo_conversation_sessions");
        session.HasIndex(static x => x.SessionId).IsUnique();
        session.HasIndex(static x => x.UserId);
        session.HasIndex(static x => x.LastActivityAtUtc);
        session.Property(static x => x.SessionId).IsRequired();
        session.Property(static x => x.UserId).HasMaxLength(256);
        session.Property(static x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        session.Property(static x => x.LastActivityAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");

        // ── Conversation Turns ──────────────────────────────────────────
        modelBuilder.ConfigureSqliteIdentity<DemoConversationTurnEntity>();
        var turn = modelBuilder.Entity<DemoConversationTurnEntity>();
        turn.ToTable("demo_conversation_turns");
        turn.HasIndex(static x => x.SessionId);
        turn.HasIndex(static x => new { x.SessionId, x.TurnId }).IsUnique();
        turn.Property(static x => x.TurnId).HasMaxLength(64).IsRequired();
        turn.Property(static x => x.UserMessage).IsRequired();
        turn.Property(static x => x.AgentResponse).IsRequired();

        // SQLite has no decimal type — store cost columns as REAL for numeric queries.
        turn.Property(static x => x.EstimatedCost).HasConversion<double>();
        turn.Property(static x => x.InputTokenCostPerMillion).HasConversion<double>();
        turn.Property(static x => x.OutputTokenCostPerMillion).HasConversion<double>();
        turn.Property(static x => x.CachedInputTokenCostPerMillion).HasConversion<double>();
        turn.Property(static x => x.EstimatedCostCurrency).HasMaxLength(8);

        // FK (SessionId → Session nav) and cascade — discovered by convention.

        // ── Agent Definitions ───────────────────────────────────────────
        var agent = modelBuilder.Entity<DemoAgentDefinitionEntity>();
        agent.ToTable("demo_agent_definitions");
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
