using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Demo.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// <see cref="IDemoUserContextProvider"/> implementation for the user-context showcase.
/// Registered as a singleton — the DB access goes through <c>IDbContextFactory&lt;DemoDbContext&gt;</c>
/// (never captures a scoped context) and the domain layer resolves the agent's scoped workflow
/// service from <see cref="IAgentExecutionScopeAccessor.Current"/>, which is the same per-circuit
/// scope the runtime adapter uses. User scoping is therefore logical (via the
/// <paramref name="userId"/> argument) plus per-circuit (Blazor Server circuit = one user).
/// </summary>
/// <remarks>
/// <para>
/// <b>Cache-aside.</b> The activity layer (persisted session counts) is queried from the DB
/// through a short-TTL <see cref="IMemoryCache"/> — a per-turn DB hit is avoided while the
/// counts stay fresh within the TTL. This is the bounded, cached ambient view the domain
/// contract expects for auto-loaded runtime context; deep enumeration belongs in lazy-loaded
/// tool calls, not here.
/// </para>
/// <para>
/// <b>Best-effort.</b> A failed DB query or a null execution scope logs and degrades to the
/// keys that ARE available (nulls are skipped by the merge) — the agent turn never fails
/// because a context read failed.
/// </para>
/// </remarks>
internal sealed class DemoUserContextProvider : IDemoUserContextProvider
{
    /// <summary>How long the activity-layer counts stay fresh before the next DB read.</summary>
    private static readonly TimeSpan ActivityCacheTtl = TimeSpan.FromSeconds(30);

    private readonly IDbContextFactory<DemoDbContext> _dbFactory;
    private readonly IAgentExecutionScopeAccessor _executionScopeAccessor;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DemoUserContextProvider> _logger;
    private readonly TimeSpan _sessionTimeout;

    /// <summary>
    /// Agent name → the scoped workflow service that exposes its live business state via
    /// <see cref="IProvideLiveUserContext"/>. This is the agent→service binding (mirrors the
    /// seed data); the live-state readers themselves live on each service, so adding a new
    /// workflow agent with live context is a one-line map entry.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Type> DomainServiceTypes =
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["Support Inbox Agent"] = typeof(SupportInboxWorkflowService),
            ["Supplier Compliance Agent"] = typeof(SupplierComplianceWorkflowService),
            ["Release Dossier Agent"] = typeof(ReleaseDossierWorkflowService),
            ["Response Orchestration Agent"] = typeof(ResponseOrchestrationWorkflowService),
        };

    public DemoUserContextProvider(
        IDbContextFactory<DemoDbContext> dbFactory,
        IAgentExecutionScopeAccessor executionScopeAccessor,
        IMemoryCache cache,
        ILogger<DemoUserContextProvider> logger,
        IOptions<DemoConversationOptions> conversationOptions)
    {
        _dbFactory = dbFactory;
        _executionScopeAccessor = executionScopeAccessor;
        _cache = cache;
        _logger = logger;
        _sessionTimeout = conversationOptions.Value.SessionTimeout;
    }

    public async Task<IReadOnlyDictionary<string, string?>> BuildAsync(
        string? userId,
        string? agentName,
        CancellationToken cancellationToken = default)
    {
        var profile = DemoUserDirectory.Resolve(userId);
        var effectiveUserId = profile.Id;

        var context = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Layer 1 — identity (deterministic, in-memory) — always present.
        context["demo.user.id"] = profile.Id;
        context["demo.user.display_name"] = profile.DisplayName;
        context["demo.user.role"] = profile.Role;
        context["demo.user.region"] = profile.Region;

        // Layers 2 + 3 — activity + domain run only for workflow agents in the domain map,
        // so agents without live business state (e.g. a just-built custom agent) skip the
        // SQL counts and the scope resolution entirely.
        if (agentName is not null && DomainServiceTypes.ContainsKey(agentName))
        {
            await AddActivityLayerAsync(context, effectiveUserId, agentName, cancellationToken).ConfigureAwait(false);
            await AddDomainLayerAsync(context, agentName, cancellationToken).ConfigureAwait(false);
        }

        return context;
    }

    private async Task AddActivityLayerAsync(
        Dictionary<string, string?> context,
        string userId,
        string? agentName,
        CancellationToken cancellationToken)
    {
        // Cache-aside: short-TTL cache keyed by (userId, agentName) — the counts are bounded
        // and change slowly, so a per-turn DB hit is wasteful.
        var cacheKey = $"demo-user-activity:{userId}:{agentName}";
        var counts = await _cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = ActivityCacheTtl;
                return await QueryActivityCountsAsync(userId, agentName, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        if (counts is null)
        {
            return; // query failed — keys stay absent (honest degradation).
        }

        context["demo.user.active_session_count"] = counts.Active.ToString();
        context["demo.user.total_session_count"] = counts.Total.ToString();
        if (counts.SessionsForAgent is not null)
        {
            context["demo.user.sessions_this_agent"] = counts.SessionsForAgent.Value.ToString();
        }
    }

    private async Task<ActivityCounts?> QueryActivityCountsAsync(
        string userId,
        string? agentName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var cutoff = DateTime.UtcNow - _sessionTimeout;

            var active = await db.Sessions.AsNoTracking()
                .CountAsync(s => s.UserId == userId && s.LastActivityAtUtc >= cutoff, cancellationToken)
                .ConfigureAwait(false);
            var total = await db.Sessions.AsNoTracking()
                .CountAsync(s => s.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            int? sessionsForAgent = null;
            if (!string.IsNullOrWhiteSpace(agentName))
            {
                // Session keys are "{baseId}::agent::{agentName}" when isolation is on —
                // a suffix match counts the user's sessions with this agent. Escape LIKE
                // wildcards so an agent name containing % or _ cannot widen the match.
                var suffix = $"%{AgentBlazor.Core.Runtime.Agents.AgentConversationScope.Separator}{EscapeLikePattern(agentName)}";
                sessionsForAgent = await db.Sessions.AsNoTracking()
                    .CountAsync(s => s.UserId == userId && EF.Functions.Like(s.SessionId, suffix), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new ActivityCounts(active, total, sessionsForAgent);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: a DB failure must not fail the agent turn — the activity keys are
            // simply absent from the context (nulls are skipped by the merge).
            _logger.LogWarning(ex, "User-context activity layer failed for user '{UserId}'.", userId);
            return null;
        }
    }

    private async Task AddDomainLayerAsync(
        Dictionary<string, string?> context,
        string agentName,
        CancellationToken cancellationToken)
    {
        if (!DomainServiceTypes.TryGetValue(agentName, out var serviceType))
        {
            return;
        }

        // Same per-circuit scope the runtime adapter uses — resolving the scoped workflow
        // service here returns the SAME live instance the agent's actions mutate.
        var scope = _executionScopeAccessor.Current;
        if (scope is null)
        {
            // Direct-adapter callers (hosted agent fallback, remote chat) have no pushed
            // execution scope — surface the degradation instead of silently returning.
            _logger.LogWarning(
                "User-context domain layer skipped for agent '{AgentName}': no execution scope available.",
                agentName);
            return;
        }

        if (scope.GetService(serviceType) is not IProvideLiveUserContext workflow)
        {
            _logger.LogWarning(
                "User-context domain layer skipped for agent '{AgentName}': scoped service '{ServiceType}' does not expose live context.",
                agentName,
                serviceType.Name);
            return;
        }

        try
        {
            var live = await workflow.GetLiveUserContextAsync(cancellationToken).ConfigureAwait(false);
            foreach (var pair in live)
            {
                context[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: a failed live-context read degrades to the keys already present.
            _logger.LogWarning(ex, "User-context domain layer failed for agent '{AgentName}'.", agentName);
        }
    }

    private sealed record ActivityCounts(int Active, int Total, int? SessionsForAgent);

    /// <summary>Escapes SQL LIKE wildcards so a literal <c>%</c>, <c>_</c>, or <c>[</c> in the
    /// agent name cannot widen the suffix match.</summary>
    private static string EscapeLikePattern(string value)
        => value
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
}