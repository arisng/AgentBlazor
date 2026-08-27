---
name: ab-conversation-store
description: "Implement conversation history storage for AgentBlazor agents, and enable/persist agent action history to a database. Use when choosing between InMemoryConversationStore, JsonFileConversationStore, or a custom durable EF Core + SQL Server store; when implementing the incremental persistence operations (UpdateTurnAsync, DeleteTurnAsync, ReorderTurnsAsync), persisting ConversationTurn.TurnId for targeted patches, wiring UseJsonFileConversationStore in a consumer app, or enabling action persistence via UseProLicense (SqliteActionHistoryStore) or implementing IActionHistoryStore against SQL Server/Postgres; or when writing EF Core action-history entities and registrations. Triggers: IConversationStore, UseConversationStore, UseJsonFileConversationStore, InMemoryConversationStore, JsonFileConversationStore, ConversationOptions, AppendTurnAsync, UpdateTurnAsync, DeleteTurnAsync, ReorderTurnsAsync, TurnId, GetHistoryAsync, ClearSessionAsync, GetActiveSessionsAsync, SetUserIdAsync, GetSessionsForUserAsync, conversation persistence, incremental persistence, ConversationTurn, ConversationHistory, SessionTimeout, IActionHistoryStore, ActionHistoryEntry, SqliteActionHistoryStore, NullActionHistoryStore, UseProLicense, agent action persistence, persist actions, action history SQL."
metadata:
    version: 0.3.0
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

## Incremental persistence model

The store contract is **incremental** — persistence never tears down and rebuilds a
conversation:

| Operation | Interface member | What it does |
|---|---|---|
| New turn | `AppendTurnAsync(sessionId, turn)` | Single append (normal turn persistence is O(1)) |
| Edit / enrichment | `UpdateTurnAsync(sessionId, turnId, turn)` | Targeted PATCH of one turn, matched by `ConversationTurn.TurnId` |
| Delete | `DeleteTurnAsync(sessionId, turnId)` | Removes a single turn by `TurnId` |
| Reorder | `ReorderTurnsAsync(sessionId, orderedTurnIds)` | Re-sequences turns (unlisted ids keep relative order at the end) |

Rules every implementation must honor:

- **Turn identity** — persist `ConversationTurn.TurnId` with each turn. Custom stores
  need a `(SessionId, TurnId)` unique index for the targeted operations.
- **Session metadata untouched** — `UpdateTurnAsync` / `DeleteTurnAsync` /
  `ReorderTurnsAsync` must not modify `SessionId`, `UserId`, `CreatedAt`, or resource
  context (`ResourceType`/`ResourceId`). Only the listed turns change.
- **`ClearSessionAsync` is user-initiated only** — it must never be invoked by the
  per-turn persistence path (`AgentChatSurface` no longer rewrites history).

### Why no full-history rewrite

Earlier versions of `AgentChatSurface` cleared the session and re-appended every turn
after each agent turn. That produced redundant I/O, non-atomic clear+re-append,
session-metadata destruction, context loss outside the execution scope, split-brain
between stores, races, and O(N) per-turn cost. The incremental model above eliminates
the rewrite entirely; `AgentChatSurface.PersistDisplayedTurnAsync` now patches the
already-persisted turn in place via `UpdateTurnAsync`.

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
    options.IncludeActionResultsInHistory = true;   // declared in this version; verify it is consumed in your library version
    options.PersistAcrossRestarts = false;   // true for JsonFile
});
```

## Enable & persist agent actions to a database

Turns are one thing; **actions** are a separate, dedicated persistence layer. After every turn, the runtime auto-records one `ActionHistoryEntry` per executed action (completed `SemanticCapability` / `UiAction` steps) through `IActionHistoryStore` — session id, user id, timestamp, user message, action id, agent id, JSON args, and result fields. Note: the current adapter records only completed/successful steps, so `Succeeded` is always `true` and `Duration`/`Route`/`ErrorMessage` are left null.

### Option A — quick enable (Pro/Enterprise, SQLite file)

```csharp
options.UseProLicense(proLicenseKey, dataDirectory: "data");
// → data/agentblazor-history.db, table `action_history` (durable, no code)
```

### Option B — shared SQL database (any tier)

Implement `IActionHistoryStore` with EF Core + SQL Server/Postgres. **There is no `UseActionHistoryStore` builder method** — registration is raw DI and **order matters**:

```csharp
// BEFORE AddAgentBlazor() — first registration wins over the library's
// TryAddSingleton<IActionHistoryStore, NullActionHistoryStore>():
builder.Services.AddSingleton<IActionHistoryStore, EfCoreActionHistoryStore>();

// ...or AFTER, using Replace:
builder.Services.Replace(ServiceDescriptor.Singleton<IActionHistoryStore, EfCoreActionHistoryStore>());
```

Rules that make or break Option B:

- **Singleton lifetime only** — the adapter (a Singleton) resolves the store from the root provider on first construction (typically at startup for hosted agents). A Scoped/Transient registration is resolved from the root: it **throws under `ValidateScopes` (Development)** or degrades to a root-lifetime instance (Production) — there is no silent Null fallback.
- Use `IDbContextFactory<T>` so the Singleton never captures a scoped `DbContext`.
- **Never throw** — the adapter catches and logs warnings; a failing store must not break the turn.
- No FK to the session table — action history is append-only analytics that survives `ClearSessionAsync`.

Full walkthrough — entity, DbContext, store, registration, migrations, multi-tenant isolation: [SQL action history](references/sql-action-history.md).

## Reference files

- [InMemory store](references/in-memory.md) — implementation details, defaults, cleanup
- [JsonFile store](references/json-file.md) — file format, load/save, atomic writes
- [EF Core + SQL Server custom store](references/ef-core-sqlserver.md) — full implementation with entities, DbContext, migrations
- [SQL action history](references/sql-action-history.md) — enable + persist agent actions (`IActionHistoryStore`) to SQL Server/Postgres, any tier
- [Fresh-scope context bridging](references/fresh-scope-context-bridging.md) — seeding fresh AsyncLocal/circuit context into singleton stores/proxies (the BFF proxy rewrite path)

## Server-side UserId rule

The store API must **never trust a client-supplied UserId**. Resolve it server-side from `ICurrentUser.GetUserId()` in every handler:

- Route-parameterized resources (`/conversations/{conversationId}*`) are **ownership-scoped**: the route id is validated to belong to the caller before any read/write.
- `/user` and `/active` collections are **self-scoped**: they always filter by the caller's own id — the client cannot query another user's conversations.
- Do not accept `UserId` on wire contracts (e.g., `AppendTurnRequest`); the caller id is the only identity source.

## Usage-record model (ConversationId-keyed)

> **Scope note:** this is the **BFF/API-layer usage contract** (e.g. Playground.Lifeline's `AgentUsageRecords`). The library's own `IUsageAnalyticsService` (`SqliteUsageAnalyticsService`) instead derives aggregates directly from the `action_history` table — these are two different things.

LLM usage analytics are stored separately from conversation turns (`AgentUsageRecord`):

- Keyed by the conversation **wire key** (`ConversationId`, the same opaque `"N"`-format GUID as the session) plus `TurnSequence` (per-turn GUID idempotency key); unique index on `(ConversationId, TurnSequence)`.
- **No FK to the session table** — usage is an append-only analytics log that must survive conversation deletion (`ClearSessionAsync`).
- `TurnSequence` is producer-assigned (`agentblazor.run_id` or a per-turn GUID from `UsageRecordingMiddleware`), never the API's turn `Sequence` counter — see `ab-middleware-authoring`.

## BFF proxy-store wiring (DI scope + resource context)

When a BFF (e.g. `Playground.Lifeline`) wires AgentBlazor's paid store interfaces to its own backend via service proxies (`AgentChatActionHistoryBffStore`, `AgentChatAuditBffService`, `AgentChatUsageBffService`), the DI lifetime must **match how AgentBlazor resolves the interface** — decompile/verify rather than guess:

- **`IActionHistoryStore` → Singleton.** `AddAgentBlazor()` registers `TryAddSingleton<IActionHistoryStore, NullActionHistoryStore>()`; the adapter consumes it via constructor injection and resolves it from the root provider on first construction (typically startup). Register proxies **before** `AddAgentBlazor()` so `TryAdd` keeps yours; registering after requires `Replace`. A Scoped/Transient proxy registration is *not* ignored — it is resolved from the root and **throws under `ValidateScopes` (Development)** or degrades to a root-lifetime instance (Production).
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
