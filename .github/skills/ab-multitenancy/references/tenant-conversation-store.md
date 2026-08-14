# Per-Tenant Conversation Store (EF Core)

## Contents

- [Strategy Choice](#strategy-choice)
- [Entities](#entities)
- [DbContext](#dbcontext)
- [Store Implementation](#store-implementation)
- [Registration](#registration)
- [Session Key Embedding](#session-key-embedding)

Custom `IConversationStore` implementation that isolates agent conversation history per tenant using EF Core + SQL Server.

## Strategy Choice

The `IConversationStore` interface has no `tenantId` parameter. Two approaches:

| Approach | Mechanism | Scope |
|---|---|---|
| **A: TenantId in session key** | Embed `{tenantId}:` prefix in `SessionId` | All methods are naturally isolated by key prefix |
| **B: TenantId column + AsyncLocal filter** | Add `TenantId` column to entities, filter every query via `TenantContextAccessor` | Full isolation, queryable per tenant |

**Recommendation:** Approach B (TenantId column) is sufficient for correct tenant isolation. Approach A (tenant prefix in SessionId) is optional defense-in-depth — it provides an additional safety net if a query forgets the TenantId filter, but is redundant with proper TenantId column filtering. Both can be combined if desired.

## Entities

> **Canonical entity definitions**: See [ab-entity-design](../../ab-entity-design/SKILL.md) for the authoritative `ConversationSessionEntity` and `ConversationTurnEntity` with all columns, indexes, and design rationale. The tenant-aware entities include:
> - `TenantId` (nvarchar(256), required) — denormalized tenant identifier on both Session and Turn
> - `BaseSessionId` (nvarchar(64), nullable) — circuit-level session identifier for grouping agent-scoped sessions
> - `AgentName` (nvarchar(256), nullable) — populated when `IsolateConversationsByAgent` is ON with multiple agents
>
> For the complete `BuildSessionKey()` → entity column mapping (including the single-agent edge case), see [ab-entity-design/references/session-identity-entities.md](../../ab-entity-design/references/session-identity-entities.md).

The store implementation below uses these columns — tenant filtering is done via `TenantContextAccessor`.

## DbContext

```csharp
public sealed class ConversationDbContext : DbContext
{
    public ConversationDbContext(DbContextOptions<ConversationDbContext> options)
        : base(options) { }

    public DbSet<ConversationSessionEntity> Sessions => Set<ConversationSessionEntity>();
    public DbSet<ConversationTurnEntity> Turns => Set<ConversationTurnEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ConversationSessionEntity>(entity =>
        {
            entity.ToTable("ConversationSessions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionId).IsUnique();
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.BaseSessionId);                    // circuit-scoped grouping
            entity.HasIndex(e => e.AgentName);                        // agent-scoped queries
            entity.HasIndex(e => new { e.TenantId, e.UserId });       // user sessions per tenant
            entity.HasIndex(e => new { e.TenantId, e.BaseSessionId });// tenant + circuit queries
            entity.HasIndex(e => e.LastActivityAtUtc);
            entity.Property(e => e.SessionId).HasMaxLength(512).IsRequired();
            entity.Property(e => e.TenantId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.BaseSessionId).HasMaxLength(64);
            entity.Property(e => e.AgentName).HasMaxLength(256);
            entity.Property(e => e.UserId).HasMaxLength(256);
        });

        builder.Entity<ConversationTurnEntity>(entity =>
        {
            entity.ToTable("ConversationTurns");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.TenantId);
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

```csharp
// Entity columns (TenantId, BaseSessionId, AgentName) — canonical definitions in ab-entity-design
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
            session = new ConversationSessionEntity
            {
                SessionId = sessionId,
                TenantId = tenantId,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Sessions.Add(session);
        }

        session.LastActivityAtUtc = DateTime.UtcNow;
        session.Turns.Add(new ConversationTurnEntity
        {
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

> **Note**: Embedding the tenant prefix in `SessionId` is optional defense-in-depth, not required for correct isolation. The `TenantId` column on `ConversationSessionEntity` + `TenantContextAccessor` (Finbuckle) provides authoritative tenant isolation. SessionId prefixing provides an additional safety net — even if a query forgets the `TenantId` filter, other tenants' sessions won't match. The trade-off is that SessionId format becomes coupled to tenant identity.
