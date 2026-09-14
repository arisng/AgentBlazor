# Store & Persistence — IAgentInspectorStore

How inspector runs are stored, which store you get by default, and how Pro persistence works.

## The contract

```csharp
public interface IAgentInspectorStore
{
    void RecordRun(InspectorRunRecord run);
    IReadOnlyList<InspectorRunRecord> GetRecentRuns(string sessionId, int limit = 20);
}
```

- `RecordRun` is called by the runtime adapter at the end of each turn.
- `GetRecentRuns(sessionId, limit)` is what the panel's **Runs** tab calls to populate its list.

Every run is an `InspectorRunRecord`:

```csharp
public sealed record InspectorRunRecord(
    string RunId,
    string SessionId,
    string AgentName,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? SystemPrompt,          // resolved from the agent registration, for the Prompt tab
    string? RawPlanResponse,       // raw plan output (unused in the standard adapter path)
    IReadOnlyList<InspectorEvent> Events,
    bool Succeeded,
    string? ErrorMessage,
    AgentExecutionPlan? ExecutionPlan = null);
```

## Which store is registered

Pick the store you need:

| Store | Registered how | Persistence | Scale | When to use |
|---|---|---|---|---|
| `NullAgentInspectorStore` | **default** (no action) | none — records nothing | — | The inspector is effectively off |
| `InMemoryAgentInspectorStore` | `UseDevTools()` | none — lost on restart | single process, dev | Development/demo debugging, no license |
| `SqliteAgentInspectorStore` | `UseProLicense(...)` | durable SQLite file | durable, survives restarts | Production debugging, Pro |

Default registration (free tier) is:

```csharp
TryAddSingleton<IAgentInspectorStore, NullAgentInspectorStore>();
```

## Enable without a license

```csharp
builder.AddAgentBlazor(options =>
{
    options.UseDevTools();            // replaces Null store with InMemoryAgentInspectorStore
    // options.UseDevTools(autoShow: true);
});
```

`UseDevTools()` (in `AgentBlazorRegistrationOptions`) does three things:

1. Replaces the `NullAgentInspectorStore` with `InMemoryAgentInspectorStore`.
2. Sets `options.EnableDevTools = true`.
3. Sets `options.AutoShowDevTools` from the `autoShow` argument (default `false`).

## Persist runs (Pro)

A Pro license swaps the store for the durable `SqliteAgentInspectorStore`:

```csharp
builder.AddAgentBlazor(options =>
{
    options.UseProLicense(licenseKey: "your-license",
                          dataDirectory: "/path/to/persistent/data");
});
```

`SqliteAgentInspectorStore` writes to an `agentblazor-inspector.db` file (table `inspector_runs`). Key behaviors:

- The connection string defaults to `Data Source=agentblazor-inspector.db`; `CreateWithPath(dbPath)` lets you point the store at an explicit path.
- Runs survive process restarts — the panel still shows past runs when you come back.
- `GetAllRecentRuns(limit = 100)` — reads across all sessions (the per-session API is `GetRecentRuns(sessionId, limit)`).
- `Prune(maxAgeDays = 30)` — retention control; call it periodically to age out old runs.

**Note:** `UseProLicense(licenseKey, dataDirectory)` requires a configured persistent writable `dataDirectory` before it can create the SQLite file. Configure it first, or the registration fails.

## Store must never break a turn

Like the conversation store, inspector recording is defensive — failures are caught and logged as warnings, so a store fault never aborts the agent turn.

## Related

- [`ab-conversation-store`](../../ab-conversation-store/SKILL.md) — same `Null`/`InMemory`/durable pattern for conversation history; useful mental model.
- `docs/internal/pricing-tiers.md` and `docs/internal/pro-tier-operations.md` — Pro/durable persistence and licensing details.