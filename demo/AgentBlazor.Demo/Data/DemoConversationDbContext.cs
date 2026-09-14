using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// SQLite-backed DbContext for the Demo's custom EF Core <c>IConversationStore</c>.
/// Registered as an <see cref="IDbContextFactory{T}"/> so the singleton store never
/// captures a scoped context.
/// </summary>
internal sealed class DemoConversationDbContext(DbContextOptions<DemoConversationDbContext> options)
    : DbContext(options)
{
    public DbSet<DemoConversationSessionEntity> Sessions => Set<DemoConversationSessionEntity>();
    public DbSet<DemoConversationTurnEntity> Turns => Set<DemoConversationTurnEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var session = modelBuilder.Entity<DemoConversationSessionEntity>();
        session.ToTable("demo_conversation_sessions");
        session.HasKey(static x => x.Id);
        session.HasIndex(static x => x.SessionId).IsUnique();
        session.HasIndex(static x => x.UserId);
        session.HasIndex(static x => x.LastActivityAtUtc);
        session.Property(static x => x.SessionId).IsRequired();
        session.Property(static x => x.UserId).HasMaxLength(256);
        session.Property(static x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        session.Property(static x => x.LastActivityAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");

        var turn = modelBuilder.Entity<DemoConversationTurnEntity>();
        turn.ToTable("demo_conversation_turns");
        turn.HasKey(static x => x.Id);
        turn.HasIndex(static x => x.SessionId);
        // Turn identity for incremental PATCH / DELETE / reorder.
        turn.HasIndex(static x => new { x.SessionId, x.TurnId }).IsUnique();
        turn.Property(static x => x.TurnId).HasMaxLength(64).IsRequired();
        turn.Property(static x => x.UserMessage).IsRequired();
        turn.Property(static x => x.AgentResponse).IsRequired();

        // Token usage + estimated cost. SQLite has no decimal type, so EF Core would
        // otherwise persist these as TEXT — which cannot be SUMmed or ORDERed
        // numerically. Store them as REAL instead so per-session cost rollups work.
        turn.Property(static x => x.EstimatedCost).HasConversion<double>();
        turn.Property(static x => x.InputTokenCostPerMillion).HasConversion<double>();
        turn.Property(static x => x.OutputTokenCostPerMillion).HasConversion<double>();
        turn.Property(static x => x.CachedInputTokenCostPerMillion).HasConversion<double>();
        turn.Property(static x => x.EstimatedCostCurrency).HasMaxLength(8);

        turn.HasOne(static x => x.Session)
            .WithMany(static s => s.Turns)
            .HasForeignKey(static x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}