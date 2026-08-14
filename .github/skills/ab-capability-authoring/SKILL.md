---
name: ab-capability-authoring
description: "Author semantic capability classes with AgentAction methods for AgentBlazor workflow agents. Use when defining [AgentCapability]-annotated classes, [AgentAction]-annotated methods, [AgentParam]-annotated parameters, returning CapabilityResult, setting approval boundaries, shaping structured outputs, warnings, and next-actions. Triggers: AgentCapabilityAttribute, AgentActionAttribute, AgentParamAttribute, CapabilityResult, RequiresApproval, WithNextActions, WithOutput, WithWarnings, AddCapability, AgentCapabilityDescriptor."
---

# `ab-capability-authoring` — Capability Authoring

## The Capability Pattern

A **capability** is a plain C# class annotated with `[AgentCapability]`. Its public methods annotated with `[AgentAction]` become tools the agent can invoke. The class is resolved from DI (scoped) — inject any services via the constructor.

```
┌─────────────────────────────────────────────┐
│  [AgentCapability("support_inbox")]          │
│  class SupportInboxCapabilities              │
│  {                                           │
│      [AgentAction("Show open tickets")]      │
│      public CapabilityResult ShowOpen()      │
│          => ...                              │
│                                              │
│      [AgentAction("Draft a reply",           │
│           RequiresApproval = true)]          │
│      public CapabilityResult DraftReply()    │
│          => ...                              │
│  }                                           │
└─────────────────────────────────────────────┘
```

## Attributes

### `[AgentCapability]` — on the class

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class AgentCapabilityAttribute : Attribute
{
    public AgentCapabilityAttribute() { }
    public AgentCapabilityAttribute(string capabilityId) { }

    public string? CapabilityId { get; }    // Stable ID. Defaults to snake_case type name, "Capabilities" suffix trimmed
    public string? Name { get; set; }       // Human-readable title
    public string? Description { get; set; } // Longer description
    public string? Category { get; set; }   // Grouping category
}
```

### `[AgentAction]` — on each method

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class AgentActionAttribute : Attribute
{
    public AgentActionAttribute() { }
    public AgentActionAttribute(string description) { }

    public string? Description { get; }     // Shown to the AI in system prompt
    public string? ActionId { get; set; }   // Override ID. Defaults to snake_case method name
    public bool RequiresApproval { get; set; }  // When true, shows AgentDialog approval UI
    public string? AvailableWhen { get; set; }  // Bool property/method name that gates availability
    public bool FollowUp { get; set; } = true;  // Whether agent should follow up after completion
    public string? Instructions { get; set; }   // Per-action behavioral guidance (ALWAYS/NEVER language)
}
```

### `[AgentParam]` — on each parameter

```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class AgentParamAttribute : Attribute
{
    public AgentParamAttribute() { }
    public AgentParamAttribute(string description) { }

    public string? Description { get; }         // Parameter description for the AI
    public bool Required { get; set; }          // When true, runtime returns clarification if missing
    public string? AllowedValues { get; set; }  // Comma-separated enum: "high,medium,low"
    public string? ContextKey { get; set; }     // Binds from runtime context instead of AI
}
```

## `CapabilityResult` — Return Type

All `[AgentAction]` methods must return `CapabilityResult` (or `Task<CapabilityResult>`).

### Factory Methods

| Factory | Properties Set | Use Case |
|---|---|---|
| `CapabilityResult.Success(summary)` | `Succeeded = true` | Happy path |
| `CapabilityResult.Failure(summary)` | `Succeeded = false` | Unexpected failure |
| `CapabilityResult.Blocked(summary)` | `Succeeded = false, IsBlocked = true` | Action cannot proceed — needs recovery |
| `CapabilityResult.InvalidArguments(summary)` | `Succeeded = false` + `Outputs["errorCode"] = "invalid_arguments"` | Bad parameters |
| `CapabilityResult.MissingArgument(name, shape, actionId)` | `Succeeded = false` + structured error outputs | Missing required param |
| `CapabilityResult.InvalidArgumentShape(name, expected, actual, actionId)` | Rich error + `WithNextAction` | Wrong param type |
| `CapabilityResult.RecoverableFailure(summary)` | `Succeeded = false` + `Outputs["errorCode"] = "recoverable_failure"` | Transient error |
| `CapabilityResult.NeedsClarification(question)` | `RequiresClarification = true` | AI needs user input |

### Result Shaping Methods

All return a new instance with added data (immutable record — `with` syntax):

```csharp
result.WithWarning("One supplier still needs review.");        // Single warning
result.WithWarnings("warning1", "warning2");                   // Multiple warnings
result.WithNextAction("Apply recovery playbook");              // Single suggested action
result.WithNextActions("Approve draft", "Submit to review");   // Multiple actions
result.WithOutput("ticketCount", 7);                            // Structured data for UI
result.WithOutputs(new Dictionary<string, object?> { ... });   // Bulk outputs
```

### Common Patterns

```csharp
// Success with guidance
return CapabilityResult.Success("Found 7 tickets needing attention")
    .WithOutput("highlightedTicketIds", new[] { "TCK-1042", "TCK-1045" })
    .WithNextActions("Draft a reply", "Explain the queue");

// Blocked with recovery path
return CapabilityResult.Blocked("Cannot prepare draft — manual review required")
    .WithWarning("One supplier still needs a human decision")
    .WithNextActions("Apply recovery playbook");

// Invalid arguments with structured error
return CapabilityResult.InvalidArguments("Review window must be 1-30 days")
    .WithOutput("parameterName", "days")
    .WithOutput("expectedShape", "integer 1-30");
```

## Full Capability Example

```csharp
[AgentCapability("support_inbox", Name = "Support Inbox",
    Description = "Review open tickets, draft replies, escalate blocked cases.",
    Category = "Workflow")]
internal sealed class SupportInboxCapabilities
{
    private readonly SupportInboxWorkflowService _workflow;
    private readonly ITenantContext _tenant;

    public SupportInboxCapabilities(
        SupportInboxWorkflowService workflow,    // resolved from DI (scoped)
        ITenantContext tenant)
    {
        _workflow = workflow;
        _tenant = tenant;
    }

    [AgentAction("Show open tickets that still need a reply",
        ActionId = "show_open_tickets")]
    public async Task<CapabilityResult> ShowOpenTicketsAsync(
        [AgentParam("Include tickets from the last N days",
            Required = false)] int days = 7)
    {
        if (days is < 1 or > 30)
        {
            return CapabilityResult.InvalidArguments(
                    "Review window must be between 1 and 30 days.")
                .WithOutput("errorCode", "invalid_review_window")
                .WithOutput("parameterName", "days")
                .WithOutput("expectedShape", "integer 1-30");
        }

        var summary = await _workflow.FocusOpenTicketsAsync(days);
        return CapabilityResult.Success(summary) with
        {
            Outputs = new Dictionary<string, object?>
            {
                ["tenantId"] = _tenant.TenantId,
                ["ticketCount"] = _workflow.VisibleTickets.Count
            }
        };
    }

    [AgentAction("Draft a reply for the highlighted tickets",
        ActionId = "draft_ticket_reply",
        RequiresApproval = true)]              // ← User must approve in AgentDialog
    public Task<CapabilityResult> DraftReplyAsync(
        [AgentParam("Optional draft direction, e.g. 'be brief'",
            Required = false)] string? tone = null)
    {
        if (!_workflow.HighlightedTicketIds.Any())
        {
            return Task.FromResult(CapabilityResult.NeedsClarification(
                "No tickets highlighted. Show open tickets first."));
        }
        return Task.FromResult(_workflow.PrepareReplyDraft(tone));
    }
}
```

## Registration

```csharp
builder.AddWorkflow<SupportInboxCapabilities>("Support Inbox Agent", agent =>
{
    agent.WithDescription("Manages ticket triage and reply drafting.");
    agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
    agent.WithRoutePrefixes("/workflows/support-inbox");
});
```

## How It Works at Runtime

1. `IAgentCapabilityRegistry` (`ReflectionAgentCapabilityRegistry`) scans registered `[AgentCapability]` types at startup
2. Discovers `[AgentAction]` methods, builds `AgentCapabilityActionDescriptor` records with JSON input schemas
3. `ChatClientRuntimeAdapter` projects them as `AITool` functions named `capability_{capabilityId}_{actionId}`
4. When the LLM calls one, `ExecuteAsync()` resolves the capability instance from DI (`ActivatorUtilities`), binds JSON arguments to method parameters (using `[AgentParam]` metadata), and invokes the method
5. The returned `CapabilityResult` is processed back to the agent as structured text

## Umbrella Capability Pattern (validated in AgentBlazor 0.2.22)

AgentBlazor 0.2.22 has **no public API for attaching multiple `[AgentCapability]` classes to a
single workflow agent**: `AddWorkflow<TCapability>` binds exactly one capability type, and
`WithAllowedCapabilityActions` is internal. When an agent needs multiple skill groups
(e.g., the in-platform Product Manager: brainstorm, PRD/spec, release notes, stakeholder
updates, roadmap), host all `[AgentAction]` methods on **one umbrella class** registered
with a single `AddWorkflow<UmbrellaCapabilities>("Agent Name", ...)`. Preserve per-skill
structure *inside* the umbrella via:

- Distinct `ActionId` values per skill group (e.g. `create_harvest`, `save_pm_artifact`,
  `lookup_lifelines`, `lookup_release_notes`).
- Per-action `Description` / `Instructions` / `RequiresApproval` expressing the skill's intent.
- A class-level `Category` (e.g. `"Product Management"`) and `Name` matching the agent name
  registered in `AddWorkflow` and any UI `LockedAgentName`.

**Agent-name consistency contract:** the `[AgentCapability(Name=...)]` value, the
`AddWorkflow<TCapability>("...")` name, any `LockedAgentName` on `AgentChatSurface`, and the
store-level agent-name constant MUST be identical. Drift breaks the agent lookup and
conversation scoping.

## Resource-Scoped Lookup Pattern (site-wide vs concrete resource)

For agents that answer from per-resource data (workspaces, sessions, files, artifacts),
take **explicit** `[AgentParam(Required=true)]` `resourceType` + `resourceId` parameters on
every data action — never read ambient request state as the primary source. Validate the
pairing through a pure decision class (see `ProductManagerLookupScopes`), and follow the
site-wide rule:

- **`resourceType="lifeline-app"` (site-wide, empty id)** → by default return
  `CapabilityResult.NeedsClarification(...)` asking the user to navigate to a concrete
  resource or provide an id — **unless** the user named a concrete resource in the chat
  (#535 relaxation): when a `search` term is present, resolve the named Lifeline
  through a site-wide `lookup_lifelines(search)` and proceed on the matched Lifeline.
  Never answer with data for a bare site-wide scope that has neither a concrete resource
  nor a resolvable name.
- **Concrete pairings** (`lifeline` + GUID, `lifeline-session` + GUID) → answer with
  tenant-scoped results, summary-first, detail on demand.

Persist the pairing contract as a server-side registry (see
`AgentChatResourcePairing`/`AgentChatResourceType` in the AgentChat module) and reject
invalid pairings (e.g. `("lifeline","")`) with 400 at the API boundary, not only in the
capability. Keep API-calling bodies thin: pairing logic is Unit-tested, transport is
Integration-tested.

### Deterministic internal grounding (validated in `run_brainstorm`)

When an action's reply must be assertable in tests (e.g. it embeds a grounding excerpt),
fetch the grounding **internally inside the action** by reusing existing helpers — do NOT
rely on the LLM making a second `lookup_*` tool call and echoing it back. The action returns
the deterministic excerpt; the LLM may call `lookup_*` only for deeper detail (progressive
disclosure). This keeps the composed-flow integration test (action → capture) deterministic
and the Unit tier testable on the pure helpers (`ProductManagerLookupScopes`).

### Bounded hierarchy walker + pure excerpt builder (validated in #531)

When an action grounds on a hierarchical domain tree (Lifeline workspace → sub-lifelines →
sessions), split the work into two halves so the renderer is Unit-testable and the walk is
deterministic:

- **Transport half — bounded BFS walker (private, in the action):** walk the hierarchy
  level-by-level with explicit caps (`MaxHierarchyDepth`, `MaxHierarchyBreadth`), fetch
  only the needed wire data (root detail, children per node, one cheap count call per
  node — e.g. `pageSize=1` → `TotalCount`). Stop expanding at the depth cap; per level,
  sort children (`OrdinalIgnoreCase`) and accept only up to the breadth budget **shared
  cumulatively across sibling parents at the same depth** (`TakeWithinBreadthBudget<T>`,
  pure static helper — budget passed `ref`, written back). Truncated children are never
  expanded, so bounded enumeration is structural, not accidental. Client failures
  (`ApiException`) propagate to the action's `try/catch` → `CapabilityResult.Failure`
  (fail-fast, never fabricate grounding).
- **Render half — pure excerpt builder (static, Unit-testable):** given the walked
  `(Name, Id, Depth, SessionCount)` nodes, render a deterministic markdown tree — root
  first, then depth-ascending → name-ascending (`OrdinalIgnoreCase`), 2-space indent per
  depth, `shortId` = first 8 chars of the id, one line per node. Empty/root-only node
  lists return short deterministic fallbacks.
- **Never fabricate:** if the owning entity (e.g. the session's lifeline) cannot be
  resolved deterministically (`OwnerType`/`OwnerId` mismatch, non-GUID id), return `null`
  excerpt and proceed without a fabricated hierarchy.

Keep next-action ids in a **pure `IReadOnlyList<string>` constant** on the lookup-scope
class (e.g. `RunBrainstormNextActions`) so the unit tier can assert the exact action set
without invoking the infrastructure-heavy capability.

### Session-aware grounding: recent-sessions + sibling-session blocks (#532/#533)

When a Lifeline (or its sessions) is the grounding entity, enrich the excerpt with
session-level context using the same transport/render split:

- **Recent-sessions excerpt (hierarchy nodes):** each hierarchy node carries a bounded
  list of session titles (`≤5`, newest-first). Effective sort timestamp is
  `StartDateTime ?? CreatedOnUtc`; total order is `(StartDateTime ?? CreatedOnUtc)` desc →
  `CreatedOnUtc` desc → `Id` desc (deterministic). `run_brainstorm` on a `lifeline` scope
  injects these as a **"Recent sessions in {Lifeline}"** seed block so the first turn is
  immediately grounded.
- **Sibling-session block (`lifeline-session` scope):** after resolving the owning
  Lifeline from the session's `OwnerType`/`OwnerId`, append the owning Lifeline's sessions
  ordered newest-first with **`← previous` / `→ next` adjacency markers** computed by a
  pure helper (`ResolveAdjacentSession`). This lets the agent answer "what is the previous
  session about?" by resolving the adjacent sibling rather than echoing the current
  session.
- **Bounded sibling window (honest truncation):** fetch only a bounded newest window
  (e.g. 50). If the current session is **outside** the window, suppress the sibling block
  entirely (never fabricate a `next`); if the fetched count is below `TotalCount`, append
  a truncation note. Implement as pure gates (`IsSessionInSiblingWindow`,
  `IsSiblingWindowTruncated`) so the unit tier can demonstrate the guard (including a
  hazard test for the pre-guard bug).
- **Never fabricate:** sibling markers require deterministic resolution — non-GUID id,
  owner mismatch, or session-outside-window all degrade honestly (block omitted + note),
  never to an invented adjacent session.

### Pure static validation helpers (validated in `ValidateBrainstormTopic`)

Parameter validation that must be Unit-tested without constructing the infrastructure-heavy
capability class (its ctor pulls `HttpClient` + generated clients) belongs in **pure static
helpers** on the umbrella class (e.g. `internal static CapabilityResult? ValidateBrainstormTopic(string? topic)`).
Wire the helper call immediately after scope resolution (before any grounding work), so
`InvalidArguments` beats both `NeedsClarification` and `Failure` for degenerate input.

### Artifact container pattern: Harvest + content-bearing HarvestFile (validated in #538)

When an agent action persists a generated artifact out of a chat conversation (e.g. the PM's
`save_pm_artifact`), use the **Harvest-HarvestFile** container, never a SessionFile:

- **House session:** resolve the owning Lifeline Session through a pure decision class
  (see `ProductManagerLookupScopes.ResolveArtifactHouseSession`). Session-scoped
  conversations reuse the current session as the house; Lifeline-scoped conversations
  implicitly create a new house session (`OwnerType="Lifeline"`, `ContentType="Documentation"`,
  `MeetingProvider="None"`, `BypassStartDateTimeValidation=true`) inside that Lifeline
  workspace. The house session is where the artifact is filed.
- **Harvest row:** create a Harvest scoped to the house session
  (`SessionId = SessionInstanceId = houseId`, `Visibility="Private"`, `Duration=0`).
- **Content-bearing HarvestFile:** persist the markdown body via the Harvest module's
  content endpoint `POST /api/v1/harvest/harvests/{harvestId}/files/content`
  (`CreateHarvestFileContent`), which writes the content to S3 and registers the
  HarvestFile row with the S3 storage key/provider. Use the **generated** OpenAPI client
  (`FilesClient.ContentPostAsync`) — never a raw `HttpClient` — once the endpoint is part
  of the generated surface.
- **Markdown files are sync-excluded by design:** `.md` is inside MediaSpace's
  `AllowedExtensionsEnum`, so content-bearing markdown HarvestFiles must be explicitly
  marked `MarkSyncNotApplicable` at creation (`CreateHarvestFileContentHandler`), not
  auto-excluded.
- **Deletion must clean S3:** content-bearing HarvestFiles carry S3 objects, so the
  delete flow must call `IHarvestFileS3Storage.DeleteAsync` (best-effort) alongside any
  MediaSpace cleanup. Prefer a combined storage-cleanup event handler that branches on
  the file's storage provider (mirror `SessionFileDeletedStorageCleanupEventHandler`).
- **Failure contract:** if the HarvestFile content persistence fails, the artifact Harvest
  still exists but the full body is not persisted — surface the failure in the action
  result rather than swallowing it.

## Feature-Flag Gating (client render gate + server enforcement)

- **Client-side:** wrap the agent's UI surfaces in a silent AND-gate component that renders
  nothing when any required tenant feature flag is OFF (see `PmAgentGate`). Never show a
  disabled placeholder or throw.
- **Server-side (do not rely on render gates alone):** gate the AgentBlazor AG-UI runtime
  endpoints on the same flags (middleware returning 404 when flags are OFF), and validate
  `AgentName` on conversation-append APIs against a registered-agent allowlist with a
  per-agent feature-flag check (403 when the tenant's flag is OFF). Client gates are UX;
  server gates are the security boundary.
