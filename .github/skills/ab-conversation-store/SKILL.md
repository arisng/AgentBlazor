---
name: ab-conversation-store
description: "Implement conversation history storage for AgentBlazor agents. Use when choosing between built-in InMemoryConversationStore, JsonFileConversationStore, or writing a custom durable implementation with EF Core + SQL Server (or any other database). Triggers: IConversationStore, UseConversationStore, UseJsonFileConversationStore, InMemoryConversationStore, JsonFileConversationStore, ConversationOptions, AppendTurnAsync, GetHistoryAsync, ClearSessionAsync, GetActiveSessionsAsync, SetUserIdAsync, GetSessionsForUserAsync, ConversationTurn, ConversationHistory, PersistAcrossRestarts, MaxTurnsPerSession, MaxHistoryInPrompt, SessionTimeout."
---

# Conversation Store — AgentBlazor

Guide for implementing and configuring conversation history storage in AgentBlazor. The system stores every user message + agent response pair so agents can maintain context across turns.

## Architecture

```
IConversationStore (interface)
├── InMemoryConversationStore  (default — ephemeral)
├── JsonFileConversationStore  (file-backed, survives restart)
└── Custom implementation      (EF Core + SQL Server, Redis, etc.)
       └── registered via UseConversationStore<TStore>() or UseConversationStore(factory)
```

The runtime consumes `IConversationStore` inside `ChatClientRuntimeAdapter`:

- On each turn, calls `AppendTurnAsync(sessionId, turn)` to persist.
- Session key is built from `sessionId + agentName` (scoped by `IsolateConversationsByAgent`).
- Failures are caught and logged as warnings — the store must **never** break the agent turn.

## Decide which store to use

| Store | Persistence | Scale | When to use |
|---|---|---|---|
| **InMemory** | None — lost on restart | Single process, <10K sessions | Development, demos, single-user apps |
| **JsonFile** | Single JSON file on disk | Single process, <1K sessions | Lightweight persistence, no database infra |
| **Custom (EF Core + SQL Server)** | Durable SQL database | Multi-process, horizontal scale, any number of sessions | Production, multi-tenant, high availability. See [ab-entity-design](../ab-entity-design/SKILL.md) for canonical entity definitions before implementing a custom store. |

## Register a store

### InMemory (default — no action needed)

```csharp
// Already registered by default in AddAgentBlazor().
// Explicit replacement if needed:
builder.UseConversationStore<InMemoryConversationStore>();
```

### JsonFile (built-in helper)

```csharp
builder.UseJsonFileConversationStore(
    filePath: "data/conversations.json",
    configure: options =>
    {
        options.MaxTurnsPerSession = 100;
        options.PersistAcrossRestarts = true;
    });
```

### Custom implementation

```csharp
builder.UseConversationStore<EfCoreConversationStore>();
// or
builder.UseConversationStore(sp => new MyCustomStore(...));
```

## Configure conversation options

All stores honor `ConversationOptions`:

```csharp
builder.Services.Configure<ConversationOptions>(options =>
{
    options.MaxTurnsPerSession = 50;        // Trim oldest turns above this
    options.MaxHistoryInPrompt = 5;         // Recent turns fed to LLM
    options.SessionTimeout = TimeSpan.FromHours(24);
    options.MaxSessions = 10000;
    options.EnableAutoCleanup = true;
    options.CleanupInterval = TimeSpan.FromHours(1);
    options.IncludeActionResultsInHistory = true;
    options.PersistAcrossRestarts = false;   // true for JsonFile
});
```

## Reference files

- [InMemory store](references/in-memory.md) — implementation details, defaults, cleanup
- [JsonFile store](references/json-file.md) — file format, load/save, atomic writes
- [EF Core + SQL Server custom store](references/ef-core-sqlserver.md) — full implementation with entities, DbContext, migrations
- [Fresh-scope context bridging](references/fresh-scope-context-bridging.md) — seeding fresh AsyncLocal/circuit context into singleton stores/proxies (the BFF proxy rewrite path)

## Server-side UserId rule

The store API must **never trust a client-supplied UserId**. Resolve it server-side from `ICurrentUser.GetUserId()` in every handler:

- Route-parameterized resources (`/conversations/{conversationId}*`) are **ownership-scoped**: the route id is validated to belong to the caller before any read/write.
- `/user` and `/active` collections are **self-scoped**: they always filter by the caller's own id — the client cannot query another user's conversations.
- Do not accept `UserId` on wire contracts (e.g., `AppendTurnRequest`); the caller id is the only identity source.

## Usage-record model (ConversationId-keyed)

LLM usage analytics are stored separately from conversation turns (`AgentUsageRecord`):

- Keyed by the conversation **wire key** (`ConversationId`, the same opaque `"N"`-format GUID as the session) plus `TurnSequence` (per-turn GUID idempotency key); unique index on `(ConversationId, TurnSequence)`.
- **No FK to the session table** — usage is an append-only analytics log that must survive conversation deletion (`ClearSessionAsync`).
- `TurnSequence` is producer-assigned (`agentblazor.run_id` or a per-turn GUID from `UsageRecordingMiddleware`), never the API's turn `Sequence` counter — see `ab-middleware-authoring`.

## BFF proxy-store wiring (DI scope + resource context)

When a BFF (e.g. `Playground.Lifeline`) wires AgentBlazor's paid store interfaces to its own backend via service proxies (`AgentChatActionHistoryBffStore`, `AgentChatAuditBffService`, `AgentChatUsageBffService`), the DI lifetime must **match how AgentBlazor resolves the interface** — decompile/verify rather than guess:

- **`IActionHistoryStore` → Singleton.** `ChatClientRuntimeAdapter` registers it via `TryAddSingleton` and resolves it from the root provider at startup. A Scoped or Transient proxy registration is **overridden by the library's own `TryAddSingleton` Null-fallback** unless you register your proxy first (order matters — register proxies **before** `AddAgentBlazor()`).
- **`IAuditLogService` / `IUsageAnalyticsService` → Scoped.** These are resolved per execution-scope, so per-request Scoped proxies are safe and are the correct lifetime.
- **Graceful no-op contracts.** Every interface member must have a real or clearly-documented no-op implementation — the store must never break the agent turn (mirror the library's Null-store convention).

### Resource context (ResourceId / ResourceType)

`AgentChatSessions.ResourceId` must never be silently empty for rows whose `ResourceType` is a session-scoped value:

- Derive `(ResourceType, ResourceId)` from the **UI context that owns the conversation**, never from a global fallback. The global site widget (no scoped `CurrentSessionId`) maps to an app-level type (e.g. `lifeline`) with **empty** `ResourceId`; the session-detail tab maps to the session-scoped type (e.g. `lifeline-session`) with a non-empty `ResourceId`.
- Avoid `?? string.Empty` fallbacks for identity columns: an unwired context silently writes empty identity data. Prefer an explicit branch on context availability so the writer either has real identity or writes a distinct app-level marker.
- **Test both paths.** Integration tests that always post an explicit `ResourceId` never exercise the global-widget fallback — add a widget-path integration/DB assertion (resource type present, identity consistent) so an unwired context surfaces as a failing test, not a data-integrity defect found in production.

### Per-identity credential cache in Singleton stores

A Singleton store/proxy that must carry circuit identity into backend calls (token capture bridging) **must never use cross-user statics**:

- Use a `ConcurrentDictionary` keyed by identity (e.g. `(UserId, TenantId)`) so each user's captured token/context is isolated.
- Seed **only the current identity's entry** from fresh scope; if identity is unresolvable, log a structured warning and skip seeding — never fall back to another user's cached credentials.
- This pattern prevents cross-user data/credential leakage when a single Singleton instance serves all circuits. See the `SingletonConversationStoreProxyTests` reference for the isolation test shape.
- **Capture in scope → restore into fresh scopes.** The proxy has two resolution branches: the pushed execution scope (`IAgentExecutionScopeAccessor.Current`) and a fresh `IServiceScopeFactory.CreateScope()`. A fresh scope inherits nothing (`HttpContext` null, `CircuitTokenCache` empty, scoped session context null) — seed it from the per-identity cache, including the session context, only when identity resolves; see [Fresh-scope context bridging](references/fresh-scope-context-bridging.md).
