---
name: ab-tool-authoring
description: "Author service tools, connect MCP servers, and configure the tool surface for AgentBlazor agents. Use when adding custom tool functions (AddTool), connecting MCP servers (UseMcpServer), defining tool parameters (AgentToolParameter), building handler delegates with DI access, filtering tools per agent (WithAllowedActions), understanding tool resolution order and execution dispatch, or diagnosing tool errors and approval gates. Triggers: AddTool, UseMcpServer, AgentServiceTool, AgentToolParameter, IAgentServiceToolRegistry, IMcpToolProvider, HttpMcpToolProvider, WithAllowedActions, tool resolution order, tool execution dispatch, EnabledToolIds, IAgentRuntimeCustomizer, RequiresApproval, tool naming, NormalizeToolName, ToolCallStart, ToolCallResult, ToolCallEnd."
metadata:
    version: 0.3.0
---

# `ab-tool-authoring` — Tool Authoring

## When to use this skill

| You need… | Use this skill |
|---|---|
| A simple function call (API lookup, DB query, string transform) | ✅ Service tool via `AddTool()` |
| To proxy an external MCP server's toolset | ✅ MCP tool via `UseMcpServer()` |
| Stateless, single-call actions with a string result | ✅ Service tool |
| To filter which tools each agent can see | ✅ `WithAllowedActions` / `IAgentRuntimeCustomizer` |
| A multi-step workflow action with DI, structured output, or approval gates | ❌ Use **`ab-capability-authoring`** instead |
| Rich results (warnings, next-actions, UI suggestions, outputs dict) | ❌ Use **`ab-capability-authoring`** instead |
| Actions that need `[AgentParam]` metadata (`ContextKey`, `AllowedValues`) | ❌ Use **`ab-capability-authoring`** instead |

**Rule of thumb:** if the action is a single function with a string return, use a service tool. If it needs structured output, approval, DI-injected services, or parameter metadata, use a capability action.

## Tool Types

| Type | Registration API | Scope | Execution |
|---|---|---|---|
| **Service Tool** | `options.AddTool(...)` | Global — filtered per agent via `WithAllowedActions` | Calls `handler(args, sp, ct)` delegate |
| **MCP Tool** | `options.UseMcpServer(url)` | Global — filtered per agent via `WithAllowedActions` | Calls MCP server via JSON-RPC |
| **Component Action** | Mounted Blazor component (auto) | Per agent via `WithAllowedComponents` | Calls `IAgentControllable.ExecuteActionAsync()` |
| **Generated UI Tool** | Built-in (always available) | All agents | Renders UI blocks (summary card, form, table, chart) |

## `AgentServiceTool` Record

```csharp
public sealed record AgentServiceTool(
    string Name,
    string Description,
    IReadOnlyList<AgentToolParameter> Parameters,
    Func<IReadOnlyDictionary<string, object?>, IServiceProvider, CancellationToken, Task<string>> Handler);
```

The `Handler` delegate receives:
- `args` — tool arguments from the LLM as a dictionary
- `sp` — application `IServiceProvider` (resolve scoped services)
- `ct` — cancellation token

## `AgentToolParameter`

```csharp
public sealed record AgentToolParameter(
    string Name,
    string Description,
    string Type = "string",     // JSON schema type — any value accepted
    bool Required = true);
```

`Type` is a free-form JSON schema type string. Common values: `"string"`, `"number"`, `"integer"`, `"boolean"`, `"array"`, `"object"`. The field is unvalidated — use whatever your LLM provider expects.

## Registering a Service Tool

On `AgentBlazorRegistrationOptions` (both overloads return `AgentBlazorRegistrationOptions` for fluent chaining):

```csharp
services.AddAgentBlazor(options =>
{
    // Async handler — resolve DI services
    options.AddTool(
        name: "lookup_ticket",
        description: "Looks up a support ticket by ID.",
        parameters: new[]
        {
            new AgentToolParameter("ticketId", "The ticket ID, e.g. TCK-1042"),
            new AgentToolParameter("includeNotes", "Include internal notes", Type = "boolean", Required = false)
        },
        handler: async (args, sp, ct) =>
        {
            var api = sp.GetRequiredService<IBffApiClient>();
            var ticketId = args.GetValueOrDefault("ticketId")?.ToString() ?? "";
            var ticket = await api.GetAsync<TicketDto>($"/api/tickets/{ticketId}", ct);
            return JsonSerializer.Serialize(ticket);
        });

    // Sync handler — convenience overload
    options.AddTool(
        name: "get_weather",
        description: "Gets the current weather for a city.",
        parameters: new[]
        {
            new AgentToolParameter("city", "City name"),
            new AgentToolParameter("units", "celsius or fahrenheit", Required = false)
        },
        handler: (args, sp) =>
        {
            var city = args.GetValueOrDefault("city")?.ToString() ?? "Unknown";
            return $"The weather in {city} is 22°C.";
        });
});
```

## Connecting an MCP Server

```csharp
services.AddAgentBlazor(options =>
{
    options.UseMcpServer("http://localhost:5000/mcp", mcp =>
    {
        mcp.IncludeTools = ["search_docs", "generate_report"];   // optional filter
        // mcp.ExcludeTools = [...];
    });
});
```

The `HttpMcpToolProvider` connects over JSON-RPC:
- Calls `tools/list` to discover tools
- Calls `tools/call` to invoke them
- Caches tool list in memory (double-checked locking with `SemaphoreSlim`)

## Per-Agent Tool Filtering

By default, agents see **all** global tools. Restrict with `WithAllowedActions`:

```csharp
options.ConfigureBuilder(builder =>
{
    // Agent A — ticket tools only
    builder.AddAgent("Support Agent", agent =>
    {
        agent.WithAllowedActions("lookup_ticket", "search_tickets");
    });

    // Agent B — weather tools only
    builder.AddAgent("Weather Agent", agent =>
    {
        agent.WithAllowedActions("get_weather", "get_forecast");
    });
});
```

The runtime checks: if `AgentRegistration.AllowedActions` is non-empty, only tools whose full name matches are projected. If empty, all tools pass through.

> **Capability actions use a separate filter path.** `WithAllowedActions` filters service/MCP tools and component actions. Capability actions (`[AgentAction]`) are filtered by `WithAllowedCapabilityActions` on the registration builder — the runtime checks `AllowedCapabilityActions` first via `IsCapabilityToolAllowed()`, then falls back to `IsNonComponentToolAllowed()`. This matters in multi-agent setups where different agents expose different capability subsets.

## Per-Turn Tool Filtering (Runtime Customization Seam)

For **runtime** (per-agent, per-conversation) tool filtering — beyond the startup-time `WithAllowedActions` — use the `IAgentRuntimeCustomizer` seam. When agent definitions come from a **database-backed registry** (Agent Builder), resolve the enabled-tool set from the persisted store keyed by `registration.Name` — see the `ab-context-assembly` [Agent Builder × customizer integration](../ab-context-assembly/SKILL.md).

```csharp
options.ConfigureBuilder(builder =>
{
    builder.AddRuntimeCustomizer<MyCustomizer>();
});

public sealed class MyCustomizer : IAgentRuntimeCustomizer
{
    public Task<AgentRuntimeCustomization?> GetRuntimeCustomizationAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken ct = default)
    {
        return Task.FromResult<AgentRuntimeCustomization?>(new AgentRuntimeCustomization(
            EnabledToolIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ticket_workflow.lookup_ticket",   // capability ActionId
                "AgentGrid.filter",                // component ComponentId.ActionId
                "get_weather"                      // service/MCP raw name
            }));
    }
}
```

## Tool Naming Normalization

`NormalizeToolName` (internal) transforms tool names for wire compatibility:

- Non-alphanumeric / non-underscore characters → `_`
- Must start with a letter — prefixed with `tool_` if not
- Names > 64 characters are truncated: `{prefix}_{tail8}_{sha256hash8}`

This matters when referencing tools in `WithAllowedActions` or `EnabledToolIds`. The **logical id** (what you register) may differ from the **normalized wire name** (what the LLM sees). Use the logical id in all configuration — the runtime resolves the mapping.

### Logical tool-id contract

`EnabledToolIds` is a whitelist of **logical ids** (not normalized wire names — `NormalizeToolName` is internal and hashes names >64 chars):

| Tool kind | Logical id | Example |
|---|---|---|
| Capability (`[AgentAction]`) | Full `ActionId` = `{capabilityId}.{localActionId}` | `ticket_workflow.lookup_ticket` |
| Component action | `ComponentId.ActionId` | `AgentGrid.filter` |
| Service / MCP | Raw registered tool name | `get_weather` |
| Generated-UI (`generated_ui_*`) | **Reserved — always projected, not filterable** | — |
| Legacy component alias | **Reserved — projected alongside its primary, not a separate filter target** | — |

- `EnabledToolIds` that is `null` **or empty** means **no filtering** (empty ≠ disable-all).
- A whitelist that matches nothing yields the existing "no available actions" response (never `Tools=[]` + `RequireAny`).
- The customizer is resolved **exactly once per turn**; standard agents (customizer returns `null`) incur zero additional work.
- See `docs/internal/runtime-customization-seam-2026-09-10.md` and `docs/internal/research/260909-runtime-adapter-feasibility.md` for the full design and evidence.

## Tool Resolution Order (in `ChatClientRuntimeAdapter`)

Tools are assembled for the LLM in this exact order:

```
1. Capability tools      ← [AgentAction] methods (workflow agents only)
2. Component action tools ← allowed by WithAllowedComponents
3. Generated UI tools    ← always (summary.card, form.draft, action.confirmation, table.view, chart.view)
4. Service tools          ← from AddTool(), filtered by WithAllowedActions
5. MCP tools              ← from UseMcpServer(), filtered by WithAllowedActions
```

All are projected as `AITool` objects into `ChatOptions.Tools`. For workflow agents, `ChatToolMode.RequireAny` is used (forces the LLM to use at least one tool per turn).

> **Note (tools + reasoning effort):** some model families (e.g. `gpt-5.6-luna`) reject tool-bearing chat-completions requests with HTTP 400 naming `reasoning_effort` unless effort is explicitly pinned. The fix is consumer-side, not tool-side: pin `ReasoningEffort.None` via `ConfigureChatOptions` — see the **`ab-provider-config` skill**.

## Approval Gates

Capability and component actions support `RequiresApproval`. When set, the runtime pauses execution, emits an `ApprovalRequired` stream event, and waits for user approval before proceeding:

```csharp
[AgentAction("Delete production data", RequiresApproval = true)]
public Task<CapabilityResult> DeleteDataAsync(...) { ... }
```

The approval flow:
1. LLM calls the tool with `RequiresApproval = true`
2. Runtime emits `AgentTurnStreamEvent.ApprovalRequired` with tool name + args
3. Chat surface renders an approval dialog (see `ab-in-chat-features`)
4. User approves → execution proceeds; user rejects → runtime returns rejection message to LLM

Generated-UI tools (`action.confirmation`) handle their own confirmation inline — they are separate from the capability approval gate.

## Tool Execution Dispatch

| Tool Type | Handler in Runtime | Delegates To |
|---|---|---|
| Capability | `InvokeCapabilityAsync()` | `IAgentCapabilityRegistry.ExecuteAsync()` → reflected `[AgentAction]` method |
| Component Action | `InvokeComponentActionAsync()` | `IComponentActionExecutor.ExecuteAsync()` → mounted Blazor component |
| Service | `InvokeServiceToolAsync()` | `tool.Handler(args, sp, ct)` — your delegate |
| MCP | `InvokeServiceToolAsync()` | `tool.Handler(...)` → `HttpMcpToolProvider.CallToolAsync()` → MCP server |
| Generated UI | `InvokeGeneratedUiToolAsync()` | Records tool call on `turnState`; `BuildDocument()` called post-turn by `RuntimeGeneratedUi` → renders block |

## Tool Streaming Events

The runtime emits `AgentTurnStreamEvent` entries for every tool call, powering the inspector and streaming UI:

| Event | When |
|---|---|
| `StepStarted` | Tool execution begins |
| `ToolCallStart` | LLM tool call received |
| `ToolCallArgs` | Arguments streaming (partial) |
| `ToolCallResult` | Handler returned a result |
| `ToolCallEnd` | Tool execution finished |
| `StepFinished` | Tool execution complete (success or failure) |
| `ApprovalRequired` | `RequiresApproval` gate hit — waiting for user |
| `ClarificationRequired` | Agent requests clarification from user |

## Tool Error Handling

| Scenario | Behavior |
|---|---|
| Service tool handler throws | Runtime catches, records `ActionOutcome.Failed`, returns error message to LLM |
| MCP `GetToolsAsync` connection failure | Returns empty tool list (retries next turn) |
| `IServiceProvider` unavailable | Returns `"IServiceProvider not available"` error to LLM |
| Generated UI validation error | Returns structured error (missing fields, invalid `chartType`, etc.) |
| Tool not found by name | LLM receives "tool not found" — no crash, no retry |

## `IAgentServiceToolRegistry` (singleton)

```csharp
public interface IAgentServiceToolRegistry
{
    IReadOnlyList<AgentServiceTool> GetTools();
    bool TryGetTool(string name, out AgentServiceTool tool);
}
```

Default implementation: `InMemoryAgentServiceToolRegistry`. When you call `AddTool()`, the tools are registered into an instance that replaces the default at startup.

## Related skills

- **`ab-capability-authoring`** — how `[AgentAction]` capability methods become tools (the sibling skill for capabilities vs service tools)
- **`ab-agent-registration`** — how agents are registered and which tools they project
- **`ab-in-chat-features`** — how generated-UI tools render in chat, including approval dialogs
- **`ab-provider-config`** — provider-level `ChatOptions` configuration (`ConfigureChatOptions`); the consumer seam that can pin reasoning effort for tool-bearing models
- **`ab-middleware-authoring`** — cross-cutting concerns in the agent turn pipeline (logging, cost control) that execute around tool dispatch
