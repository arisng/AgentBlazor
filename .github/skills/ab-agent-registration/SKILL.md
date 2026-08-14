---
name: ab-agent-registration
description: "Register agents and workflow-capability agents with AgentBlazor. Use when defining agent names, descriptions, instructions, route bindings, component access, tool access, data schemas, and metadata. Triggers: AddAgent, AddWorkflow, AgentRegistrationBuilder, WithRoutePrefixes, WithAllowedComponents, WithAllowedActions, WithAllowedCapabilityActions, WithDataSchemas, WithInstructions, WithToolsFromAssembly, ConfigureBuilder, AgentBlazorBuilder."
---

# `ab-agent-registration` — Agent Registration

## Overview

Two agent types exist:

| Type | API | Agent gets access to |
|---|---|---|
| **Standalone agent** | `AddAgent(name, configure)` | Global service tools, component actions, generated UI tools |
| **Workflow agent** | `AddWorkflow<TCapability>(name, configure)` | All of the above + `[AgentAction]` methods from the capability class |

Internally, `AddWorkflow` calls `AddCapability(type)` (registers the `[AgentCapability]` class for reflection scanning) then `AddAgent(name, builder => builder.WithAllowedCapabilityActions(actionIds))` — the agent is locked to those capability actions.

## `AgentBlazorBuilder` (returned by `ConfigureBuilder`)

```csharp
// Inside options.ConfigureBuilder(builder => { ... })
```

| Method | Purpose |
|---|---|
| `AddAgent(name, configure?)` | Register a standalone agent. `name` must be non-empty. |
| `AddWorkflow<TCapability>(name, configure?)` | Register a capability type + agent locked to its actions. Requires `[AgentCapability]` on the type. |
| `AddCapability<TCapability>()` / `AddCapability(type)` | Register a capability without creating an agent. Used by components. |
| `AddDataSchema(schemaSet)` / `AddDataSchema(factory)` | Register an `AgentDataSchemaSet` for entity-aware agents. See `WithDataSchemas`. |
| `ConfigureComponentCatalog(configure)` | Configure default component capabilities (presets: Minimal / Full). |
| `EnablePromptTracing(configure?)` | Enable observability tracing on all prompt requests. |

## `AgentRegistrationBuilder` (configure delegate parameter)

```csharp
builder.AddAgent("Support Agent", agent =>
{
    agent.WithDescription("...");
    agent.WithInstructions(systemPrompt);
    agent.WithRoutePrefixes("/workflows/support");
    agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
    agent.WithAllowedActions("someComponent.someAction");
    agent.WithDataSchemas("support-data");
    agent.WithToolsFromAssembly(typeof(SomeTool).Assembly);
    agent.WithMetadata("key", "value");
});
```

| Method | Signature | Notes |
|---|---|---|
| `WithDescription` | `(string description)` | Shown to the AI in system prompts |
| `WithInstructions` | `(string instructions)` | System prompt for this agent |
| `WithRoutePrefixes` | `params string[] routePrefixes` | Stored in `Metadata["route_prefixes"]`. Longest-prefix match at runtime |
| `WithAllowedComponents` | `params string[] componentIds` | Restricts which Blazor components this agent can control |
| `WithAllowedActions` | `params string[] componentActionIds` | Format `"ComponentId.ActionId"`. Or use tuple overload |
| `WithDataSchemas` | `params string[] schemaNames` | Names must match `AddDataSchema(...)` registrations |
| `WithToolsFromAssembly` | `(Assembly assembly)` | Defined but **not yet consumed** by the runtime |
| `WithMetadata` | `(string key, string value)` | Arbitrary metadata. `route_prefixes` is set via `WithRoutePrefixes` |

## `AgentRegistration` (built output)

```csharp
public sealed class AgentRegistration
{
    public string Name { get; init; }                              // "Support Agent"
    public string? Description { get; init; }
    public string? Instructions { get; init; }
    public IReadOnlySet<string> AllowedComponents { get; init; }   // "AgentDataGrid"
    public IReadOnlySet<string> AllowedActions { get; init; }      // "componentId.actionId"
    public IReadOnlySet<string> AllowedCapabilityActions { get; }  // "capabilityId.actionId"
    public IReadOnlyList<string> ToolAssemblyNames { get; init; }
    public IReadOnlySet<string> AllowedDataSchemas { get; init; }  // "support-data"
    public IReadOnlyDictionary<string, string> Metadata { get; }   // "route_prefixes" etc.
}
```

## Route Locking

When `LockAgentToCurrentRoute` is true on the chat surface, the UI resolves the best agent by longest-prefix match:

```
Route: /workflows/support-inbox
  Agent A: route_prefixes = ["/workflows"]               → match length 11
  Agent B: route_prefixes = ["/workflows/support-inbox"]  → match length 26 ← wins
```

On the server, `RuntimeTurnPreflight.AllowsLockedRoute()` validates the same match before executing. If no agent matches the current route and `agent_lock` is requested, the turn returns an error.

## Agent Selection Priority (server side)

`ChatClientRuntimeAdapter.ResolveAgentRegistration()`:

```
1. Explicit agent name from request → validate route match
2. Context agent name from Blazor UI → validate route match
3. If agent-lock requested but no match → return null (error)
4. Implicit fallback → first agent alphabetically
```

## Middleware Registration

Register pipeline-level middleware on `AgentBlazorRegistrationOptions`:

```csharp
options.UseMiddleware<TenantCostControlMiddleware>();  // typed, resolved from DI
options.UseMiddleware(async (ctx, next, ct) =>         // inline delegate
{
    ctx.Items["TenantId"] = "...";
    await next(ct);
});
```

Middlewares execute in registration order (first = outermost). See `ab-middleware-authoring`.

## Complete Minimal Example

```csharp
services.AddAgentBlazor(options =>
{
    options.UseOpenAI(apiKey);
    // Optional: pin provider-level ChatOptions (e.g. gpt-5.6-family tools need
    // ReasoningEffort.None to avoid HTTP 400 reasoning_effort rejections) —
    // see the ab-provider-config skill.
    // options.ConfigureChatOptions(o => o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None });
    options.ConfigureBuilder(builder =>
    {
        // Standalone agent — has access to global service tools + components
        builder.AddAgent("Support Agent", agent =>
        {
            agent.WithDescription("Helps operators triage support requests.");
            agent.WithRoutePrefixes("/workflows/support");
            agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
            agent.WithDataSchemas("support-data");
        });

        // Workflow agent — locked to specific [AgentAction] methods
        builder.AddWorkflow<SupportInboxCapabilities>("Support Inbox Agent", agent =>
        {
            agent.WithDescription("Manages ticket triage and reply drafting.");
            agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
            agent.WithRoutePrefixes("/workflows/support-inbox");
        });
    });
});
```

## Entity Persistence (Future)

Agent registrations are currently held in-memory only (`AgentRegistrationBuilder` → `AgentRegistration`). For store-backed registration persistence (e.g., loading agent configs from a database), see [ab-entity-design](../ab-entity-design/SKILL.md) for entity design patterns including:

- Modeling agent metadata (name, description, instructions) as relational entities
- Component allowlists and action IDs as related tables or JSON columns
- Data schema references with foreign keys to schema registrations
- Multitenancy considerations for per-tenant agent registrations
- Migration strategy for evolving agent configurations alongside code changes

## Related skills

- **`ab-provider-config`** — provider-level `ChatOptions` configuration (`ConfigureChatOptions`), the seam that adjusts the transport options the agents' instructions/tools ride on
- **`ab-capability-authoring`** — authoring `[AgentCapability]`/`[AgentAction]` classes for workflow agents
- **`ab-tool-registration`** — global service/MCP tools and per-agent filtering
- **`ab-cli`** — onboarding existing solutions with `agentblazor init`/`analyze`/`scaffold`
