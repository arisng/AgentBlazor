---
name: ab-tool-registration
description: "Register service tools and MCP server tools for AgentBlazor agents. Use when adding custom tool functions (AddTool), connecting MCP servers (UseMcpServer), defining tool parameters (AgentToolParameter), building handler delegates with DI access, and filtering tools per agent (WithAllowedActions). Triggers: AddTool, UseMcpServer, AgentServiceTool, AgentToolParameter, IAgentServiceToolRegistry, IMcpToolProvider, HttpMcpToolProvider, WithAllowedActions."
---

# `ab-tool-registration` — Tool Registration

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
    string Type = "string",     // JSON schema type
    bool Required = true);
```

## Registering a Service Tool

On `AgentBlazorRegistrationOptions`:

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

## Tool Execution Dispatch

| Tool Type | Handler in Runtime | Delegates To |
|---|---|---|
| Capability | `InvokeCapabilityAsync()` | `IAgentCapabilityRegistry.ExecuteAsync()` → reflected `[AgentAction]` method |
| Component Action | `InvokeComponentActionAsync()` | `IComponentActionExecutor.ExecuteAsync()` → mounted Blazor component |
| Service | `InvokeServiceToolAsync()` | `tool.Handler(args, sp, ct)` — your delegate |
| MCP | `InvokeServiceToolAsync()` | `tool.Handler(...)` → `HttpMcpToolProvider.CallToolAsync()` → MCP server |
| Generated UI | `InvokeGeneratedUiToolAsync()` | `IAgentUiToolCatalog.BuildDocument()` → renders block |

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

- **`ab-provider-config`** — provider-level `ChatOptions` configuration (`ConfigureChatOptions`); the consumer seam that can pin reasoning effort for tool-bearing models
- **`ab-agent-registration`** — how agents are registered and which tools they project
- **`ab-capability-authoring`** — how `[AgentAction]` capability methods become tools
- **`ab-in-chat-features`** — how generated-UI tools render in chat
