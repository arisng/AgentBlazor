# Per-Tenant Conversation Store (EF Core)

## Contents

- [Design Principle](#design-principle)
- [Strategy Choice](#strategy-choice)
- [Consumer-Derived Entities](#consumer-derived-entities)
- [DbContext](#dbcontext)
- [Store Implementation](#store-implementation)
- [Registration](#registration)
- [Session Key Embedding](#session-key-embedding)

Custom `IConversationStore` implementation that isolates agent conversation history per tenant using EF Core.

## Design Principle

**The library base entities (`ConversationSessionEntity`, `ConversationTurnEntity`) do NOT include `TenantId`.** Multitenancy is a consumer app concern — you inherit from the abstract base classes and add `TenantId`, then wire global query filters or manual filtering in your store.

> See [SKILL.md § Core Design Principle](../SKILL.md#core-design-principle-multitenancy-is-a-consumer-app-extension) for the full rationale and the library base entity properties.

## Strategy Choice

The `IConversationStore` interface has no `tenantId` parameter. Two approaches:

| Approach | Mechanism | Scope |
|---|---|---|
| **A: TenantId in session key** | Embed `{tenantId}:` prefix in `SessionId` | All methods are naturally isolated by key prefix |
| **B: TenantId column + AsyncLocal filter** | Add `TenantId` column to consumer-derived entities, filter every query via `TenantContextAccessor` | Full isolation, queryable per tenant |

**Recommendation:** Approach B (TenantId column on derived entities) is sufficient for correct tenant isolation. Approach A (tenant prefix in SessionId) is optional defense-in-depth — it provides an additional safety net if a query forgets the TenantId filter, but is redundant with proper TenantId column filtering. Both can be combined if desired.

## Consumer-Derived Entities

The consumer app inherits from the library base entities and adds `TenantId`. Optional extension columns (`BaseSessionId`, `AgentName`) are also consumer additions — the library does NOT define them.

```csharp
using AgentBlazor.Core.Persistence;

// Consumer app entity — inherits all base columns from ConversationSessionEntity:
//   Guid Id, string SessionId, string? UserId, DateTime CreatedAtUtc,
//   DateTime LastActivityAtUtc, List<ConversationTurnEntity> Turns
public sealed class TenantSessionEntity : ConversationSessionEntity
{
    /// <summary>Denormalized tenant identifier — filter on every query.</summary>
    public required string TenantId { get; set; }

    /// <summary>Optional: circuit-level session identifier for grouping agent-scoped sessions.</summary>
    public string? BaseSessionId { get; set; }
}

// Consumer app entity — inherits all base columns from ConversationTurnEntity:
//   Guid Id, Guid SessionId (FK), string TurnId, string UserMessage, string AgentResponse,
//   string? PlannedActionsJson, string? ExecutionResultsJson, string? ExecutionPlanJson,
//   string? GeneratedUiJson, DateTime TimestampUtc, int TurnSequence,
//   long? PromptTokens, long? CompletionTokens, long? TotalTokens, long? CachedInputTokens,
//   decimal? EstimatedCost, string? EstimatedCostCurrency,
//   decimal? InputTokenCostPerMillion, decimal? OutputTokenCostPerMillion,
//   decimal? CachedInputTokenCostPerMillion, ConversationSessionEntity? Session
public sealed class TenantTurnEntity : ConversationTurnEntity
{
    /// <summary>Denormalized tenant identifier — filter on every query.</summary>
    public required string TenantId { get; set; }
}
```

> **Note:** `AgentName` is NOT a column on the base entity. The runtime composes session keys with `::agent::AgentName` suffix via `AgentConversationScope.BuildSessionKey()`. Adding `AgentName` as a column is a consumer optimization for indexed queries — see the agent design skill for details.

The store implementation below uses these derived entity types — tenant filtering is done via `TenantContextAccessor`.

## DbContext

The consumer's DbContext uses **derived entity types** (not the library base types) and configures `TenantId` indexing:

```csharp
using AgentBlazor.Core.Persistence;

public sealed class ConversationDbContext : DbContext
{
    public ConversationDbContext(DbContextOptions<ConversationDbContext> options)
        : base(options) { }

    // Use consumer-derived types, NOT the abstract base classes
    public DbSet<TenantSessionEntity> Sessions => Set<TenantSessionEntity>();
    public DbSet<TenantTurnEntity> Turns => Set<TenantTurnEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<TenantSessionEntity>(entity =>
        {
            entity.ToTable("ConversationSessions");
            entity.HasKey(e => e.Id);                                  // Guid Id (from base)
            entity.HasIndex(e => e.SessionId).IsUnique();              // string SessionId (from base)
            entity.HasIndex(e => e.TenantId);                          // consumer extension
            entity.HasIndex(e => e.BaseSessionId);                     // consumer extension (optional)
            entity.HasIndex(e => new { e.TenantId, e.UserId });       // user sessions per tenant
            entity.HasIndex(e => new { e.TenantId, e.BaseSessionId });// tenant + circuit queries
            entity.HasIndex(e => e.LastActivityAtUtc);                 // from base
            entity.Property(e => e.SessionId).HasMaxLength(512).IsRequired();
            entity.Property(e => e.TenantId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.BaseSessionId).HasMaxLength(64);
            entity.Property(e => e.UserId).HasMaxLength(256);
        });

        builder.Entity<TenantTurnEntity>(entity =>
        {
            entity.ToTable("ConversationTurns");
            entity.HasKey(e => e.Id);                                  // Guid Id (from base)
            entity.HasIndex(e => e.SessionId);                         // Guid FK (from base)
            entity.HasIndex(e => e.TenantId);                          // consumer extension
            entity.Property(e => e.TenantId).HasMaxLength(256).IsRequired();
            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Turns)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

## Store Implementation

The store uses the consumer's derived entity types (`TenantSessionEntity`, `TenantTurnEntity`) and filters all queries by `TenantId` from the `TenantContextAccessor`:

```csharp
public sealed class TenantConversationStore : IConversationStore, IDisposable
{
    private readonly TenantContextAccessor _tenantAccessor;
    private readonly ConversationOptions _options;
    private readonly IDbContextFactory<ConversationDbContext> _dbFactory;
    private Timer? _cleanupTimer;

    public TenantConversationStore(
        TenantContextAccessor tenantAccessor,
        IOptions<ConversationOptions> options,
        IDbContextFactory<ConversationDbContext> dbFactory)
    {
        _tenantAccessor = tenantAccessor;
        _options = options.Value;
        _dbFactory = dbFactory;
        if (_options.EnableAutoCleanup)
            _cleanupTimer = new Timer(_ => _ = CleanupAsync(), null,
                _options.CleanupInterval, _options.CleanupInterval);
    }

    private string CurrentTenantId =>
        _tenantAccessor.TenantContext?.TenantId
        ?? throw new InvalidOperationException("No tenant context.");

    public async Task<ConversationHistory?> GetHistoryAsync(
        string sessionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenantId = CurrentTenantId;

        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Turns)
            .FirstOrDefaultAsync(s =>
                s.SessionId == sessionId && s.TenantId == tenantId, ct);

        if (session is null) return null;

        if (DateTime.UtcNow - session.LastActivityAtUtc > _options.SessionTimeout)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync(ct);
            return null;
        }

        return MapToHistory(session);
    }

    public async Task AppendTurnAsync(
        string sessionId, ConversationTurn turn, CancellationToken ct = default)
    {
        var tenantId = CurrentTenantId;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var session = await db.Sessions
            .Include(s => s.Turns)
            .FirstOrDefaultAsync(s =>
                s.SessionId == sessionId && s.TenantId == tenantId, ct);

        if (session is null)
        {
            session = new TenantSessionEntity
            {
                SessionId = sessionId,
                TenantId = tenantId,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Sessions.Add(session);
        }

        session.LastActivityAtUtc = DateTime.UtcNow;
        session.Turns.Add(new TenantTurnEntity
        {
                    TurnId = turn.TurnId,
                    TenantId = tenantId,
                    UserMessage = turn.UserMessage,
                    AgentResponse = turn.AgentResponse,
                    TimestampUtc = turn.Timestamp
                });

        if (session.Turns.Count > _options.MaxTurnsPerSession)
        {
            var excess = session.Turns.OrderBy(t => t.TimestampUtc)
                .Take(session.Turns.Count - _options.MaxTurnsPerSession).ToList();
            db.Turns.RemoveRange(excess);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var tenantId = CurrentTenantId;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions
            .FirstOrDefaultAsync(s =>
                s.SessionId == sessionId && s.TenantId == tenantId, ct);
        if (session is not null)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync(ct);
        }
    }

        public async Task<bool> UpdateTurnAsync(
            string sessionId, string turnId, ConversationTurn turn, CancellationToken ct = default)
        {
            var tenantId = CurrentTenantId;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var session = await db.Sessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId, ct);
            if (session is null) return false;

            var entity = await db.Turns
                .FirstOrDefaultAsync(t => t.SessionId == session.Id && t.TurnId == turnId, ct);
            if (entity is null) return false;

            // Targeted PATCH — content only; TurnId/Timestamp/session metadata preserved.
            entity.UserMessage = turn.UserMessage;
            entity.AgentResponse = turn.AgentResponse;
            entity.PlannedActionsJson = ...;
            entity.ExecutionResultsJson = ...;
            entity.ExecutionPlanJson = ...;
            entity.GeneratedUiJson = ...;
            session.LastActivityAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteTurnAsync(
            string sessionId, string turnId, CancellationToken ct = default)
        {
            var tenantId = CurrentTenantId;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var session = await db.Sessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId, ct);
            if (session is null) return false;

            var entity = await db.Turns
                .FirstOrDefaultAsync(t => t.SessionId == session.Id && t.TurnId == turnId, ct);
            if (entity is null) return false;

            db.Turns.Remove(entity);
            session.LastActivityAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }

        public async Task ReorderTurnsAsync(
            string sessionId, IReadOnlyList<string> orderedTurnIds, CancellationToken ct = default)
        {
            var tenantId = CurrentTenantId;
            if (orderedTurnIds.Count == 0) return;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var session = await db.Sessions
                .Include(s => s.Turns)
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId, ct);
            if (session is null) return;

            var byId = session.Turns.ToDictionary(t => t.TurnId, StringComparer.OrdinalIgnoreCase);
            var ordered = orderedTurnIds.Where(byId.ContainsKey).Select(id => byId[id])
                .Concat(session.Turns.Where(t => !orderedTurnIds.Contains(t.TurnId, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            for (var i = 0; i < ordered.Count; i++) ordered[i].TurnSequence = i;
            session.LastActivityAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

    public async Task<IReadOnlyCollection<string>> GetActiveSessionsAsync(
        CancellationToken ct = default)
    {
        var tenantId = CurrentTenantId;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow - _options.SessionTimeout;
        return await db.Sessions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.LastActivityAtUtc >= cutoff)
            .Select(s => s.SessionId)
            .ToListAsync(ct);
    }

    public async Task SetUserIdAsync(
        string sessionId, string userId, CancellationToken ct = default)
    {
        var tenantId = CurrentTenantId;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions
            .FirstOrDefaultAsync(s =>
                s.SessionId == sessionId && s.TenantId == tenantId, ct);
        if (session is not null)
        {
            session.UserId = userId;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyCollection<string>> GetSessionsForUserAsync(
        string userId, CancellationToken ct = default)
    {
        var tenantId = CurrentTenantId;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow - _options.SessionTimeout;
        return await db.Sessions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.UserId == userId
                     && s.LastActivityAtUtc >= cutoff)
            .Select(s => s.SessionId)
            .ToListAsync(ct);
    }

    // ... MapToHistory, MapToTurn, CleanupAsync, Dispose omitted for brevity
    // (standard EF Core store patterns from ab-conversation-store skill)
}
```

## Registration

```csharp
builder.Services.AddDbContextFactory<ConversationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Shared")!));

// AgentBlazor builder
ab.UseConversationStore(sp => new TenantConversationStore(
    sp.GetRequiredService<TenantContextAccessor>(),
    sp.GetRequiredService<IOptions<ConversationOptions>>(),
    sp.GetRequiredService<IDbContextFactory<ConversationDbContext>>()));
```

## Session Key Embedding

The `AgentChatSurface` component builds session keys via `AgentConversationScope.BuildSessionKey()`. To add tenant prefixing, set `SessionId` to include the tenant:

```razor
<AgentChatSurface SessionId="@($"{Tenant.TenantId}:{CircuitSessionId}")" />
```

Then `AgentConversationScope` produces keys like:
`d1e9a3f2b8c04a5e9d7f6c1b2a3d4e5f::agent::Support Inbox Agent`

This means even if tenant filtering is missed in a query, the session key itself is tenant-scoped.

> **Note**: Embedding the tenant prefix in `SessionId` is optional defense-in-depth, not required for correct isolation. The `TenantId` column on the consumer's `TenantSessionEntity` (derived from `ConversationSessionEntity`) + `TenantContextAccessor` provides authoritative tenant isolation. SessionId prefixing provides an additional safety net — even if a query forgets the `TenantId` filter, other tenants' sessions won't match. The trade-off is that SessionId format becomes coupled to tenant identity.
