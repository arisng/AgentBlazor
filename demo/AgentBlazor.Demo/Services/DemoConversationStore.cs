using System.Text.Json;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Demo.Data;
using AgentBlazor.Execution;
using AgentBlazor.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Custom EF Core <c>IConversationStore</c> for the Demo — the production-database
/// pattern the incremental persistence model targets. Registers as a singleton via
/// <c>IDbContextFactory&lt;DemoConversationDbContext&gt;</c> so it never captures a
/// scoped context.
/// <para>
/// Demonstrates the full incremental contract: turns are appended once per turn and
/// edited/deleted/reordered by <c>TurnId</c> without ever clearing and rebuilding the
/// session. Session metadata (<c>UserId</c>, creation) is preserved by every
/// operation.
/// </para>
/// </summary>
internal sealed class DemoConversationStore : IConversationStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<DemoConversationDbContext> _dbFactory;
    private readonly ConversationOptions _options;
    private readonly Timer? _cleanupTimer;

    public DemoConversationStore(
        IDbContextFactory<DemoConversationDbContext> dbFactory,
        IOptions<ConversationOptions>? options = null)
    {
        _dbFactory = dbFactory;
        _options = options?.Value ?? new ConversationOptions();

        if (_options.EnableAutoCleanup)
        {
            _cleanupTimer = new Timer(
                _ => _ = CleanupExpiredSessionsAsync(),
                null,
                _options.CleanupInterval,
                _options.CleanupInterval);
        }
    }

    public async Task<ConversationHistory?> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Turns)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session is null)
        {
            return null;
        }

        if (DateTime.UtcNow - session.LastActivityAtUtc > _options.SessionTimeout)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        return MapToHistory(session);
    }

    public async Task AppendTurnAsync(
        string sessionId,
        ConversationTurn turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(turn);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var session = await db.Sessions
            .Include(s => s.Turns)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session is null)
        {
            session = new DemoConversationSessionEntity
            {
                SessionId = sessionId,
                CreatedAtUtc = DateTime.UtcNow,
                LastActivityAtUtc = DateTime.UtcNow
            };
            db.Sessions.Add(session);
        }

        session.LastActivityAtUtc = DateTime.UtcNow;

        var nextSequence = session.Turns.Count > 0
            ? session.Turns.Max(static t => t.TurnSequence) + 1
            : 0;

        session.Turns.Add(new DemoConversationTurnEntity
        {
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
            TimestampUtc = turn.Timestamp,
            TurnSequence = nextSequence
        });

        // Trim oldest turns if over limit.
        if (session.Turns.Count > _options.MaxTurnsPerSession)
        {
            var excess = session.Turns
                .OrderBy(t => t.TurnSequence)
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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var session = await db.Sessions
            .Include(s => s.Turns)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        var byId = session.Turns.ToDictionary(
            static t => t.TurnId,
            static t => t,
            StringComparer.OrdinalIgnoreCase);

        // Re-sequence listed turns in the requested order; unlisted turns keep their
        // existing relative order after the listed ones.
        var reordered = orderedTurnIds
            .Where(byId.ContainsKey)
            .Select(turnId => byId[turnId])
            .Concat(session.Turns.Where(
                t => !orderedTurnIds.Contains(t.TurnId, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        for (var i = 0; i < reordered.Count; i++)
        {
            reordered[i].TurnSequence = i;
        }

        session.LastActivityAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var cutoff = DateTime.UtcNow - _options.SessionTimeout;

        return await db.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.LastActivityAtUtc >= cutoff)
            .Select(s => s.SessionId)
            .ToListAsync(cancellationToken);
    }

    private static ConversationHistory MapToHistory(DemoConversationSessionEntity session)
    {
        return new ConversationHistory
        {
            SessionId = session.SessionId,
            UserId = session.UserId,
            CreatedAt = session.CreatedAtUtc,
            LastActivityAt = session.LastActivityAtUtc,
            Turns = session.Turns
                .OrderBy(static t => t.TurnSequence)
                .ThenBy(static t => t.TimestampUtc)
                .Select(MapToTurn)
                .ToArray()
        };
    }

    private static ConversationTurn MapToTurn(DemoConversationTurnEntity entity)
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
            await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);

            var cutoff = DateTime.UtcNow - _options.SessionTimeout;
            var expired = await db.Sessions
                .Where(s => s.LastActivityAtUtc < cutoff)
                .ToListAsync(CancellationToken.None);

            if (expired.Count > 0)
            {
                db.Sessions.RemoveRange(expired);
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch
        {
            // Store failures must never break the agent turn.
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}