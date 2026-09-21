# User-Scoped Runtime Context

How a consumer app gets **per-user business data into every agent turn** — the end-to-end
journey: user identity plumbing → the `UserContext` seam → a consumer-owned async provider →
the disciplines that keep it bounded, cached, and honest.

**Demo grounding:** the working reference implementation lives in
`demo/AgentBlazor.Demo/Services/` — `DemoUserContextProvider.cs`,
`IDemoUserContextProvider.cs`, `IProvideLiveUserContext.cs`, `DemoUserDirectory.cs` — and is
exercised from the Agent Builder page's "Chat as user" picker. The code below mirrors it with
consumer-generic names.

---

## 1. User identity plumbing — who is chatting?

The chat component (`AgentChatSurface`) builds the `AgentTurnRequest` on every turn. It needs
to know **who is chatting** so the per-turn request carries the user identity. The host app
owns that identity (auth provider, a picker, a query param — the library must not assume a
scheme), so the surface exposes an optional `UserId` parameter:

```razor
<AgentChatSurface Title="Support Inbox"
                  DefaultAgentName="Support Inbox Agent"
                  SessionId="demo:agent-builder"
                  UserId="@_chatUserId" />   <!-- host-owned identity -->
```

The surface threads it into every `AgentTurnRequest` (send, clarification, approval
continuation). The request resolves the effective user id with a fixed precedence:

```
AgentTurnRequest.GetEffectiveUserId()
    ├── explicit UserId (typed channel — set by the surface from the UserId parameter)
    ├── Context["agentblazor.user_id"] (fallback for context-only callers, e.g. remote chat)
    └── null (no identity — providers fall back to a default)
```

**Contract:** `UserId` is `string?` — when unset, behavior is byte-identical to before (the
provider resolves your default user). When set, it flows to `request.GetEffectiveUserId()`,
which is the key the customizer uses to scope the context.

---

## 2. The `UserContext` seam

The customizer (`IAgentRuntimeCustomizer`) returns an `AgentRuntimeCustomization` whose
**`UserContext`** member carries the per-user business context for this turn:

```csharp
public Task<AgentRuntimeCustomization?> GetRuntimeCustomizationAsync(
    AgentRegistration registration,
    AgentTurnRequest request,
    CancellationToken cancellationToken = default)
{
    var tools = _registry.TryGetRuntimeCustomization(registration.Name);     // tool whitelist
    var userContext = await _userContextProvider
        .BuildAsync(request.GetEffectiveUserId(), registration.Name, cancellationToken)
        .ConfigureAwait(false);

    return userContext.Count == 0 && tools is null
        ? null
        : new AgentRuntimeCustomization(EnabledToolIds: tools?.EnabledToolIds,
                                        UserContext: userContext);
}
```

**Merge semantics** (verified in the adapter's `MergeUserContext`):

- User context lands in the turn's user message **"Runtime context:" block** (not the system
  prompt) — applied in **both streaming and non-streaming** paths.
- **Channel-supplied `AgentTurnRequest.Context` keys win on collision** — the host's explicit
  context is never overwritten by the provider.
- **`null` / empty values are skipped** — a failed sub-query degrades honestly instead of
  rendering `"key: "`.

The seam is **per-agent, per-user**: resolved exactly once per turn, keyed by the resolved
registration and the effective user id.

---

## 3. The consumer-owned async provider

The library ships the seam, not the provider. Consumers define a small interface and an
implementation. The contract is **async** because real implementations query a database or
cache; nullable values align with `UserContext` (nulls are skipped by the merge).

```csharp
/// <summary>
/// Implemented by services that expose LIVE user-scoped business state.
/// Null values are skipped by the merge (honest degradation).
/// </summary>
public interface IProvideLiveUserContext
{
    Task<IReadOnlyDictionary<string, string?>> GetLiveUserContextAsync(
        CancellationToken cancellationToken = default);
}
```

The provider composes **three layers** — identity (deterministic), activity (real persisted
counts), domain (live business state):

```csharp
public sealed class UserContextProvider
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAgentExecutionScopeAccessor _executionScopeAccessor;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserContextProvider> _logger;

    // Agent name → the scoped service exposing its live business state.
    private static readonly IReadOnlyDictionary<string, Type> DomainServiceTypes =
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["Support Inbox Agent"] = typeof(SupportInboxWorkflowService),
            ["Supplier Compliance Agent"] = typeof(SupplierComplianceWorkflowService),
        };

    public async Task<IReadOnlyDictionary<string, string?>> BuildAsync(
        string? userId, string? agentName, CancellationToken ct = default)
    {
        var context = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Layer 1 — identity (deterministic, in-memory; unknown users → default profile).
        context["demo.user.id"] = userId ?? "default-user";
        context["demo.user.role"] = ResolveRole(userId);

        // Layers 2 + 3 — only for agents with live business state (skips SQL otherwise).
        if (agentName is not null && DomainServiceTypes.ContainsKey(agentName))
        {
            await AddActivityLayerAsync(context, userId, agentName, ct).ConfigureAwait(false);
            await AddDomainLayerAsync(context, agentName, ct).ConfigureAwait(false);
        }

        return context;
    }

    private async Task AddActivityLayerAsync(
        Dictionary<string, string?> context, string? userId, string? agentName, CancellationToken ct)
    {
        // Cache-aside: short-TTL keyed by (userId, agentName) — counts change slowly.
        var counts = await _cache.GetOrCreateAsync(
            $"user-activity:{userId}:{agentName}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                return await QueryActivityCountsAsync(userId, agentName, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        if (counts is null) return; // failed query → keys stay absent (honest).
        context["user.active_session_count"] = counts.Active.ToString();
        context["user.total_session_count"] = counts.Total.ToString();
    }

    private async Task<ActivityCounts?> QueryActivityCountsAsync(
        string? userId, string? agentName, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);
            var active = await db.Sessions.AsNoTracking()
                .CountAsync(s => s.UserId == userId && s.LastActivityAtUtc >= cutoff, ct)
                .ConfigureAwait(false);
            var total = await db.Sessions.AsNoTracking()
                .CountAsync(s => s.UserId == userId, ct)
                .ConfigureAwait(false);
            return new ActivityCounts(active, total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: a DB failure must not fail the agent turn.
            _logger.LogWarning(ex, "User-context activity layer failed for '{UserId}'.", userId);
            return null;
        }
    }

    private async Task AddDomainLayerAsync(
        Dictionary<string, string?> context, string agentName, CancellationToken ct)
    {
        if (!DomainServiceTypes.TryGetValue(agentName, out var serviceType)) return;

        // Same per-circuit scope the runtime adapter uses — the SAME live instance the
        // agent's actions mutate. Surface the degradation instead of silently returning.
        var scope = _executionScopeAccessor.Current;
        if (scope is null)
        {
            _logger.LogWarning("Domain layer skipped for '{AgentName}': no execution scope.", agentName);
            return;
        }

        if (scope.GetService(serviceType) is not IProvideLiveUserContext workflow)
        {
            _logger.LogWarning("Domain layer skipped for '{AgentName}': no live-context service.", agentName);
            return;
        }

        try
        {
            var live = await workflow.GetLiveUserContextAsync(ct).ConfigureAwait(false);
            foreach (var pair in live) context[pair.Key] = pair.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Domain layer failed for '{AgentName}'.", agentName);
        }
    }

    private sealed record ActivityCounts(int Active, int Total);
}
```

**DI wiring** (singleton-safe — the DB goes through `IDbContextFactory`, never a captured
scoped context):

```csharp
builder.Services.AddMemoryCache();                                        // cache-aside activity
builder.Services.AddSingleton<UserContextProvider>();                     // the provider
// The customizer (singleton) injects the provider; the domain layer resolves the agent's
// scoped workflow service from IAgentExecutionScopeAccessor.Current at turn time.
```

**The interface implemented by each live-state service** (a workflow service, a repository,
anything with mutable business state):

```csharp
internal sealed class SupportInboxWorkflowService : IProvideLiveUserContext
{
    public Task<IReadOnlyDictionary<string, string?>> GetLiveUserContextAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyDictionary<string, string?>>(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["support_inbox.open_tickets"] = VisibleTickets.Count.ToString(),
                ["support_inbox.escalated"] = EscalatedTicketIds.Count.ToString(),
            });
    }
}
```

---

## 4. The three disciplines

These are the contract, not style advice:

1. **Bounded** — counts / `TOP N` only. Never scan a full table into the prompt. The agent
   needs the ambient view, not your warehouse.
2. **Cache-aside** — short-TTL cache for hot counts so a per-turn DB hit is avoided while
   values stay fresh within the TTL (the Demo uses a 30s `IMemoryCache`). A per-turn DB query
   adds latency to **every** agent turn.
3. **Best-effort / Never-Fabricate** — a failed query returns `null` for its keys (skipped by
   the merge) or leaves them absent; log the degradation. **The agent turn never fails because
   a context read failed**, and the LLM never sees fabricated numbers.

### Why the disciplines are also a cost model

`UserContext` lands in the **user message tail** (layer 5 of the context stack), never the
system prompt — so per-user volatile data **never invalidates the LLM's cached
system+schema+tools prefix** (layers 1–3, billed at the cached-token rate, ~5% of input in
the Demo's pricing). The disciplines keep the tail cheap:

| Discipline | Cost effect |
|---|---|
| **Bounded** | Small tail → fewer uncached input tokens per turn |
| **Cache-aside** | Values stable within TTL → fewer cache misses on the tail |
| **Byte-stable keys** | Deterministic block → the tail itself can hit the cache across turns |
| **Best-effort** | Nulls skipped → no churn from failed reads |

The old `Instructions`-in-system-prompt model broke the cached prefix on every persona
change; the re-frame (persona merged at hydration, `UserContext` in the tail) is what makes
per-user context cache-friendly. See the SKILL.md "KV cache & token cost" section for the
full layer table.

---

## 5. Two-speed grounding boundary

The AgentChat domain contract's two-speed grounding applies here:

- **Auto-loaded ambient** (what this reference builds): shallow, bounded, cached per-user
  counts injected every turn — the "what's my state right now" view.
- **Lazy-loaded deep** (never injected): full documents, long lists, drill-down detail —
  fetched **on demand via tool calls** (`lookup_session` + `fileId` style), never pre-composed
  into the prompt.

If you find yourself injecting a large enumeration, you've crossed the boundary — move it to a
tool call and keep the ambient view small.

---

## 6. Middleware boundary — customizer vs `IAgentTurnMiddleware`

Both can put data into the user message. The split:

| Concern | Seam | Why |
|---|---|---|
| **Per-agent, per-user business context** | Customizer `UserContext` | Resolved once per turn, keyed by agent + user; can also narrow tools; the seam's re-framed purpose |
| **Cross-cutting pipeline concerns** (auth, tenant enrichment, rate limits, audit) | `IAgentTurnMiddleware` | Must run regardless of which agent; can short-circuit; sees the whole pipeline |

Use the **customizer** for "this user's business data for this agent". Use **middleware** for
"every turn must pass through this gate". See `ab-middleware-authoring` for the middleware
side; the Demo's `TenantEnrichmentMiddleware`-style pattern is the reference for cross-cutting
injection.

---

## Related

- `ab-context-assembly` SKILL.md — the pipeline, the `AgentRuntimeCustomizer` seam, the Agent
  Builder integration
- `ab-chat-composer` — the `AgentChatSurface.UserId` parameter (identity plumbing)
- `ab-middleware-authoring` — the cross-cutting alternative
- `ab-agent-builder` — the persona (user-managed instructions) is a DISTINCT concern; it is
  merged into `AgentRegistration.Instructions` at hydration, never constructed at chat runtime