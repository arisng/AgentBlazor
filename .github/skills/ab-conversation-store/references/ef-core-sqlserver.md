# EF Core + SQL Server custom conversation store

Full implementation of `IConversationStore` using Entity Framework Core with SQL Server. Durable, scalable, suitable for production and multi-tenancy.

## Table of contents

1. [Required packages](#required-packages)
2. [Entity models](#entity-models)
3. [DbContext](#dbcontext)
4. [Store implementation](#store-implementation)
5. [Registration](#registration)
6. [Migrations](#migrations)
7. [Advanced: multi-tenant isolation](#advanced-multi-tenant-isolation)

---

## Required packages

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
```

Or for any other EF Core provider (PostgreSQL, SQLite, etc.) — the implementation is provider-agnostic.

---

## Entity models

> **Canonical entity definitions**: See [ab-entity-design](../../ab-entity-design/SKILL.md) for the authoritative `ConversationSessionEntity` and `ConversationTurnEntity` with all columns, types, nullability, indexes, and design rationale. Key columns include:
> - `BaseSessionId` (nvarchar(64), nullable) — circuit-level session identifier for grouping agent-scoped sessions
> - `AgentName` (nvarchar(256), nullable) — populated when `IsolateConversationsByAgent` is ON with multiple agents
> - `TenantId` (nvarchar(256), required) — denormalized tenant identifier for multitenancy queries

The EF Core store implementation (`EfCoreConversationStore` below) references entity properties through the canonical model.

---

## DbContext

```csharp
public class ConversationDbContext : DbContext
{
    public ConversationDbContext(DbContextOptions<ConversationDbContext> options)
        : base(options) { }

    public DbSet<ConversationSessionEntity> Sessions => Set<ConversationSessionEntity>();
    public DbSet<ConversationTurnEntity> Turns => Set<ConversationTurnEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationSessionEntity>(entity =>
        {
            entity.ToTable("ConversationSessions");

            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionId).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.LastActivityAtUtc);  // for cleanup queries
            entity.HasIndex(e => e.BaseSessionId);           // circuit-scoped session grouping
            entity.HasIndex(e => e.AgentName);                // agent-scoped session queries
            entity.HasIndex(e => e.TenantId);                 // tenant-scoped queries
            entity.HasIndex(e => new { e.TenantId, e.UserId }); // user session browsing per tenant

            entity.Property(e => e.SessionId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.UserId).HasMaxLength(256);
        });

        modelBuilder.Entity<ConversationTurnEntity>(entity =>
        {
            entity.ToTable("ConversationTurns");

            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionId);
                    entity.HasIndex(e => new { e.SessionId, e.TurnId }).IsUnique();  // turn identity for incremental ops

                    entity.Property(e => e.TurnId).HasMaxLength(64).IsRequired();
                    entity.Property(e => e.UserMessage).IsRequired();
                    entity.Property(e => e.AgentResponse).IsRequired();

                    entity.HasOne(e => e.Session)
                          .WithMany(s => s.Turns)
                          .HasForeignKey(e => e.SessionId)
                          .OnDelete(DeleteBehavior.Cascade);
                });
    }
}
```

### Index strategy

| Index | Purpose |
|---|---|
| `IX_SessionId` (unique) | Fast `GetHistoryAsync` lookups |
| `IX_UserId` | `GetSessionsForUserAsync` |
| `IX_LastActivityAtUtc` | Expired-session cleanup queries |
| `IX_SessionId` on Turns | Efficient turn retrieval by session |

---

## Store implementation

```csharp
using System.Text.Json;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Execution;
using AgentBlazor.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class EfCoreConversationStore : IConversationStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<ConversationDbContext> _dbContextFactory;
    private readonly ConversationOptions _options;
    private readonly ILogger<EfCoreConversationStore>? _logger;
    private Timer? _cleanupTimer;
    private bool _disposed;

    public EfCoreConversationStore(
        IDbContextFactory<ConversationDbContext> dbContextFactory,
        IOptions<ConversationOptions>? options = null,
        ILogger<EfCoreConversationStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        _dbContextFactory = dbContextFactory;
        _options = options?.Value ?? new ConversationOptions();
        _logger = logger;

        if (_options.EnableAutoCleanup)
        {
            _cleanupTimer = new Timer(
                _ => _ = CleanupExpiredSessionsAsync(),
                null,
                _options.CleanupInterval,
                _options.CleanupInterval);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cleanupTimer?.Dispose();
        _disposed = true;
    }

    // ─── IConversationStore ───────────────────────────────────────

    public async Task<ConversationHistory?> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Turns)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session is null)
            return null;

        // Check expiry
        if (DateTime.UtcNow - session.LastActivityAtUtc > _options.SessionTimeout)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        // Sort turns on the client side
                session.Turns = session.Turns.OrderBy(t => t.TurnSequence).ThenBy(t => t.TimestampUtc).ToList();

        return MapToHistory(session);
    }

    public async Task AppendTurnAsync(
        string sessionId,
        ConversationTurn turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(turn);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var session = await db.Sessions
            .Include(s => s.Turns)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session is null)
        {
            session = new ConversationSessionEntity
            {
                SessionId = sessionId,
                CreatedAtUtc = DateTime.UtcNow,
                LastActivityAtUtc = DateTime.UtcNow
            };
            db.Sessions.Add(session);
        }

        session.LastActivityAtUtc = DateTime.UtcNow;

        session.Turns.Add(new ConversationTurnEntity
        {
            SessionId = session.Id,
                    TurnId = turn.TurnId,
                    UserMessage = turn.UserMessage,
                    AgentResponse = turn.AgentResponse,
                    PlannedActionsJson = SerializeIfAny(turn.PlannedActions),
                    ExecutionResultsJson = SerializeIfAny(turn.ExecutionResults),
                    ExecutionPlanJson = turn.ExecutionPlan is not null
                        ? JsonSerializer.Serialize(turn.ExecutionPlan, JsonOptions)
                        : null,
                    GeneratedUiJson = turn.GeneratedUi is not null
                        ? JsonSerializer.Serialize(turn.GeneratedUi, JsonOptions)
                        : null,
                    TimestampUtc = turn.Timestamp
                });

        // Trim oldest turns if over limit
        if (session.Turns.Count > _options.MaxTurnsPerSession)
        {
            var excess = session.Turns
                .OrderBy(t => t.TimestampUtc)
                .Take(session.Turns.Count - _options.MaxTurnsPerSession)
                .ToList();

            db.Turns.RemoveRange(excess);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session is not null)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

        public async Task<bool> UpdateTurnAsync(
            string sessionId,
            string turnId,
            ConversationTurn turn,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
            ArgumentNullException.ThrowIfNull(turn);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var session = await db.Sessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
            if (session is null)
            {
                return false;
            }

            var entity = await db.Turns
                .FirstOrDefaultAsync(
                    t => t.SessionId == session.Id && t.TurnId == turnId,
                    cancellationToken);
            if (entity is null)
            {
                return false;
            }

            // Targeted PATCH: replace content only. TurnId, Timestamp and session
            // metadata are preserved.
            entity.UserMessage = turn.UserMessage;
            entity.AgentResponse = turn.AgentResponse;
            entity.PlannedActionsJson = SerializeIfAny(turn.PlannedActions);
            entity.ExecutionResultsJson = SerializeIfAny(turn.ExecutionResults);
            entity.ExecutionPlanJson = turn.ExecutionPlan is not null
                ? JsonSerializer.Serialize(turn.ExecutionPlan, JsonOptions)
                : null;
            entity.GeneratedUiJson = turn.GeneratedUi is not null
                ? JsonSerializer.Serialize(turn.GeneratedUi, JsonOptions)
                : null;

            session.LastActivityAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> DeleteTurnAsync(
            string sessionId,
            string turnId,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(turnId);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var session = await db.Sessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
            if (session is null)
            {
                return false;
            }

            var entity = await db.Turns
                .FirstOrDefaultAsync(
                    t => t.SessionId == session.Id && t.TurnId == turnId,
                    cancellationToken);
            if (entity is null)
            {
                return false;
            }

            db.Turns.Remove(entity);
            session.LastActivityAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task ReorderTurnsAsync(
            string sessionId,
            IReadOnlyList<string> orderedTurnIds,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentNullException.ThrowIfNull(orderedTurnIds);
            if (orderedTurnIds.Count == 0)
            {
                return;
            }

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var session = await db.Sessions
                .Include(s => s.Turns)
                .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
            if (session is null)
            {
                return;
            }

            var byId = session.Turns.ToDictionary(t => t.TurnId, t => t, StringComparer.OrdinalIgnoreCase);

            // Re-sequence listed turns in the requested order; unlisted turns keep their
            // relative order after the listed ones.
            var sequence = 0;
            var ordered = orderedTurnIds
                .Where(byId.ContainsKey)
                .Select(turnId => byId[turnId])
                .Concat(session.Turns.Where(t => !byId.ContainsKey(t.TurnId) || !orderedTurnIds.Contains(t.TurnId, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                // Advance a per-turn sequence counter stored alongside each turn (e.g. TurnSequence).
                ordered[i].TurnSequence = sequence++;
            }

            session.Turns = ordered;
            session.LastActivityAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

    public async Task<IReadOnlyCollection<string>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var cutoff = DateTime.UtcNow - _options.SessionTimeout;

        return await db.Sessions
            .AsNoTracking()
            .Where(s => s.LastActivityAtUtc >= cutoff)
            .Select(s => s.SessionId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetUserIdAsync(
        string sessionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session is not null)
        {
            session.UserId = userId;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<string>> GetSessionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var cutoff = DateTime.UtcNow - _options.SessionTimeout;

        return await db.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.LastActivityAtUtc >= cutoff)
            .Select(s => s.SessionId)
            .ToListAsync(cancellationToken);
    }

    // ─── Helpers ──────────────────────────────────────────────────

    private static ConversationHistory MapToHistory(ConversationSessionEntity session)
    {
        return new ConversationHistory
        {
            SessionId = session.SessionId,
            UserId = session.UserId,
            CreatedAt = session.CreatedAtUtc,
            LastActivityAt = session.LastActivityAtUtc,
            Turns = session.Turns.Select(MapToTurn).ToArray()
        };
    }

    private static ConversationTurn MapToTurn(ConversationTurnEntity entity)
    {
        return new ConversationTurn
        {
                TurnId = entity.TurnId,
                Timestamp = entity.TimestampUtc,
                UserMessage = entity.UserMessage,
                AgentResponse = entity.AgentResponse,
                PlannedActions = DeserializeOrEmpty<PlannedComponentAction>(entity.PlannedActionsJson),
                                ExecutionResults = DeserializeOrEmpty<ComponentActionExecutionResult>(entity.ExecutionResultsJson),
                ExecutionPlan = entity.ExecutionPlanJson is not null
                    ? JsonSerializer.Deserialize<AgentExecutionPlan>(entity.ExecutionPlanJson, JsonOptions)
                    : null,
                GeneratedUi = entity.GeneratedUiJson is not null
                    ? JsonSerializer.Deserialize<AgentUiDocument>(entity.GeneratedUiJson, JsonOptions)
                    : null
            };
        }

    private static string? SerializeIfAny<T>(IReadOnlyList<T> list)
        => list.Count > 0 ? JsonSerializer.Serialize(list, JsonOptions) : null;

    private static IReadOnlyList<T> DeserializeOrEmpty<T>(string? json)
        => json is not null
                ? JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? []
            : [];

    private async Task CleanupExpiredSessionsAsync()
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);

            var cutoff = DateTime.UtcNow - _options.SessionTimeout;
            var expired = await db.Sessions
                .Where(s => s.LastActivityAtUtc < cutoff)
                .ToListAsync(CancellationToken.None);

            if (expired.Count > 0)
            {
                db.Sessions.RemoveRange(expired);
                await db.SaveChangesAsync(CancellationToken.None);
                _logger?.LogInformation("Cleaned up {Count} expired sessions", expired.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clean up expired sessions");
        }
    }
}
```

---

## Registration

```csharp
// 1. Register DbContext factory (SQL Server)
builder.Services.AddDbContextFactory<ConversationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ConversationStore")));

// 2. Replace the conversation store
builder.Services.AddAgentBlazor(options =>
{
    // ...provider config...
})
.ConfigureBuilder(ab => ab.UseConversationStore<EfCoreConversationStore>());

// 3. (Optional) Tune conversation options
builder.Services.Configure<ConversationOptions>(options =>
{
    options.MaxTurnsPerSession = 200;
    options.MaxHistoryInPrompt = 10;
    options.SessionTimeout = TimeSpan.FromDays(7);
    options.PersistAcrossRestarts = true;
});
```

### appsettings.json

```json
{
  "ConnectionStrings": {
    "ConversationStore": "Server=(localdb)\\mssqllocaldb;Database=AgentBlazor_Conversations;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

---

## Migrations

```bash
# Install dotnet-ef if needed
dotnet tool install --global dotnet-ef

# Create initial migration
dotnet ef migrations add InitialConversationStore \
    --context ConversationDbContext

# Apply to database
dotnet ef database update \
    --context ConversationDbContext
```

### Idempotent SQL script (for CI/CD)

```bash
dotnet ef migrations script \
    --context ConversationDbContext \
    --output scripts/conversation-store-migration.sql
```

---

## Multi-tenant isolation

Add a `TenantId` column to entities and filter all queries. The canonical `ConversationSessionEntity` and `ConversationTurnEntity` in [ab-entity-design](../../ab-entity-design/SKILL.md) already include `TenantId` with proper indexes.

```csharp
// Every store method filters by tenant:
.Where(s => s.SessionId == sessionId && s.TenantId == _tenantId)
```

For the complete multitenancy entity patterns — composite keys, global query filters, compound indexes, and tenant deletion cascade — see [ab-entity-design/references/multitenancy-patterns.md](../../ab-entity-design/references/multitenancy-patterns.md).
