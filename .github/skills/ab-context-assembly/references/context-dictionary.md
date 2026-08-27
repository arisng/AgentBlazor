# Context Dictionary — Runtime Context Injection

The context dictionary is the primary mechanism for injecting per-request runtime data into every agent turn. This reference covers all built-in keys, the known user-message format, and patterns for custom injection.

## Table of contents

- [Context Dictionary — Runtime Context Injection](#context-dictionary--runtime-context-injection)
  - [Table of contents](#table-of-contents)
  - [Built-in keys: `AgentRuntimeContextKeys`](#built-in-keys-agentruntimecontextkeys)
  - [Known user-message format](#known-user-message-format)
    - [Teaching agents to use runtime context](#teaching-agents-to-use-runtime-context)
  - [Consuming context in capability actions](#consuming-context-in-capability-actions)
    - [How it works](#how-it-works)
    - [Built-in keys available for binding](#built-in-keys-available-for-binding)
    - [Missing context at invocation time](#missing-context-at-invocation-time)
    - [Relationship to the user message](#relationship-to-the-user-message)
  - [How chat components populate context](#how-chat-components-populate-context)
    - [`AgentChatSurface` and `AgentChatWidget`](#agentchatsurface-and-agentchatwidget)
    - [`AgentChatBar`](#agentchatbar)
  - [Adding custom context](#adding-custom-context)
    - [Via middleware (recommended)](#via-middleware-recommended)
    - [Via middleware (only option)](#via-middleware-only-option)
  - [Generated UI context](#generated-ui-context)
  - [Handoff context](#handoff-context)
  - [Best practices](#best-practices)

---

## Built-in keys: `AgentRuntimeContextKeys`

All keys are defined as `const string` fields on the `AgentRuntimeContextKeys` class (namespace: `AgentBlazor.Core.Runtime.Agents`):

| Constant | Value | Set by | Purpose |
|---|---|---|---|
| `SessionId` | `agentblazor.session_id` | Runtime (automatic) | Current session identifier |
| `RunId` | `agentblazor.run_id` | Runtime (automatic) | Current run/turn identifier |
| `UserId` | `agentblazor.user_id` | Runtime (automatic) | Authenticated user identifier |
| `AgentName` | `agentblazor.agent_name` | Chat component | Currently selected/locked agent |
| `AgentLock` | `agentblazor.agent_lock` | Chat component | `"True"` when agent selection is locked |
| `AgentHandoffFrom` | `agentblazor.agent_handoff_from` | Chat component | Agent being handed off from |
| `AgentHandoffTo` | `agentblazor.agent_handoff_to` | Chat component | Agent being handed off to |
| `AgentHandoffAt` | `agentblazor.agent_handoff_at` | Chat component | ISO 8601 timestamp of handoff |
| `CurrentRoute` | `agentblazor.current_route` | Chat component | Current browser URL path |
| `ContextVersion` | `agentblazor.context_version` | Runtime (automatic) | Version identifier for context schema |
| `ProjectLegacyComponentToolAliases` | `agentblazor.project_legacy_component_tool_aliases` | Runtime (automatic) | Legacy tool alias mappings |
| `SharedStateSnapshot` | `agentblazor.shared_state_snapshot` | Runtime (automatic) | Snapshot of shared state at turn start |
| `SharedStateDelta` | `agentblazor.shared_state_delta` | Runtime (automatic) | Delta of shared state changes |

---

## Known user-message format

Context dictionary entries are automatically appended to the user message in this format:

```
[user's typed message goes here unchanged]

Runtime context:
- agentblazor.agent_lock: True
- agentblazor.agent_name: support
- agentblazor.current_route: /tickets/42
- agentblazor.session_id: sess_abc123
- myapp.department: engineering
- myapp.user_role: admin
```

Rules:
- Keys are sorted alphabetically (case-insensitive).
- Each line is `- key: value`.
- The "Runtime context:" heading only appears when the context dictionary is non-empty.
- Custom keys (anything not starting with `agentblazor.`) are mixed in with built-in keys — no separation.

### Teaching agents to use runtime context

Since the context dictionary is injected into the user message (not the system prompt), you must tell the agent to read it:

```csharp
agent.WithInstructions("""
    At the start of every turn, scan the "Runtime context:" section
    in the user message. Use it to determine:
    - The current page (agentblazor.current_route) for context-appropriate responses
    - Any custom keys (myapp.*) for application-specific data
    """);
```

---

## Consuming context in capability actions

While the "Runtime context:" block is text injected into the user message for the LLM to *read*, capability actions can also **programmatically bind** to context dictionary values using `ContextKey` on `[AgentParam]`.

### How it works

When you set `ContextKey` on a parameter, two things happen:

1. **Schema omission** — The parameter is hidden from the tool definition the LLM sees. The model cannot supply, hallucinate, or override it.
2. **Runtime injection** — At invocation time, the runtime looks up `request.Context[contextKey]` and binds the value automatically.

```csharp
[AgentAction("Escalate this ticket")]
public async Task<CapabilityResult> EscalateAsync(
    [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
    [AgentParam(ContextKey = AgentRuntimeContextKeys.RunId)] string runId,
    [AgentParam("Priority level", Required = true)] string priority)
{
    // sessionId and runId are auto-injected — the model never sees them
    // priority must be supplied by the LLM
}
```

### Built-in keys available for binding

| `AgentRuntimeContextKeys` constant | Key value | Typical use |
|---|---|---|
| `SessionId` | `agentblazor.session_id` | Identify the chat session |
| `RunId` | `agentblazor.run_id` | Identify the current run/turn |
| `UserId` | `agentblazor.user_id` | Identify the authenticated user |
| `CurrentRoute` | `agentblazor.current_route` | React to the current page URL |
| `AgentName` | `agentblazor.agent_name` | Know which agent is active |

Custom keys injected via middleware are also valid — the `ContextKey` value can be any string that matches a key in the context dictionary.

### Missing context at invocation time

If a `ContextKey`-bound parameter cannot be resolved (the key is absent from the context dictionary), the runtime returns a structured error:

```
CapabilityResult.InvalidArguments(
    "Required runtime context 'agentblazor.session_id' is missing for capability action 'escalate'.")
    .WithOutput("errorCode", "missing_runtime_context")
    .WithOutput("parameterName", "sessionId")
    .WithOutput("contextKey", "agentblazor.session_id")
    .WithOutput("actionId", "escalate")
    .WithOutput("expectedShape", "string")
    .WithNextAction("Ensure runtime context 'agentblazor.session_id' is supplied before invoking 'escalate'.")
```

The action is **never invoked** with a null or missing value — the runtime short-circuits before execution.

### Relationship to the user message

Context-bound parameters and the "Runtime context:" text block are **complementary, not competing**:

| Mechanism | Target | Purpose |
|---|---|---|
| `ContextKey` on `[AgentParam]` | The capability action method | Programmatic injection — the C# code gets the value directly |
| "Runtime context:" in user message | The LLM | The model can *read* the same keys for reasoning and planning |

Both read from the same `request.Context` dictionary. A key like `agentblazor.session_id` can appear in the user message *and* be bound via `ContextKey` — they don't conflict.

For full details on authoring `[AgentParam]` with `ContextKey`, see [`ab-capability-authoring` — ContextKey](../../ab-capability-authoring/SKILL.md#contextkey--binding-parameters-from-runtime-context).

---

## How chat components populate context

### `AgentChatSurface` and `AgentChatWidget`

Automatically set these keys:

| Condition | Key set |
|---|---|
| Always (if `EnableGeneratedUi`) | `GenerateUiContextKey` = `"True"` |
| Always | `CurrentRoute` = current URL path (without query string) |
| Agent selection is locked | `AgentName` = selected agent, `AgentLock` = `"True"` |
| Handoff is pending | `AgentHandoffFrom`, `AgentHandoffTo`, `AgentHandoffAt` |

> **Note:** Custom runtime context cannot be injected via a chat component parameter. Use [middleware](#via-middleware-recommended) instead.

### `AgentChatBar`

Same as above, minus handoff support (the bar is simpler).

---

## Adding custom context

### Via middleware (recommended)

```csharp
public class CustomContextMiddleware : IAgentTurnMiddleware
{
    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct)
    {
        context.Request.Context ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        context.Request.Context["myapp.feature_flags"] = "beta_search:on,new_sidebar:off";
        context.Request.Context["myapp.environment"] = "staging";
        await next(ct);
    }
}
```

Register it:
```csharp
options.UseMiddleware<CustomContextMiddleware>();
```

### Via middleware (only option)

Currently, the chat components (`AgentChatSurface`, `AgentChatWidget`, `AgentChatBar`) do **not** expose a public parameter for arbitrary additional context keys. The only supported mechanism for injecting custom runtime context is middleware (shown above).

If your agent needs per-page or per-component context (e.g., the current section of the app), you have two options:

1. **Route-aware middleware** — read the current URL from DI (e.g., `NavigationManager.Uri`) and inject it into the context dictionary in a middleware.
2. **Custom chat page** — if you build your own chat page, call `AgentTurnRequest` with a custom context dictionary directly.

---

## Generated UI context

When `EnableGeneratedUi="true"` on the chat component, the package sets a special key:

```csharp
context[AgentGenerativeUiSpec.GenerateUiContextKey] = "True";
```

The actual key value is `"agentblazor.ui.generate"`. Use the constant `AgentGenerativeUiSpec.GenerateUiContextKey` rather than the string literal to avoid mismatches.

When a user interacts with a generated-UI card (clicking a button, submitting a form), the action metadata is appended to the user message **before** the runtime context:

```
[original user message or empty]

Generated UI action context:
BlockId: approval_card_42
ActionId: approve
Prompt: Approve refund for order #1234
Payload: {"ticketId":"T-5678","amount":"49.99"}

Runtime context:
- ...
```

The agent receives this as part of the user message and can use it to determine which action to execute.

---

## Handoff context

During agent handoff, the chat component sets three keys:

- `AgentHandoffFrom` — the agent that initiated the handoff
- `AgentHandoffTo` — the target agent
- `AgentHandoffAt` — ISO 8601 timestamp

These keys are temporary — they're only present during the handoff turn. The target agent can read them to understand the context of the transfer.

---

## Best practices

1. **Prefix custom keys** with your app name (e.g., `myapp.*`) to avoid collisions with future `agentblazor.*` keys.

2. **Keep values short**. Context lines are part of the LLM input and consume tokens. Avoid serialized JSON blobs in context values — use minimal identifiers and let the agent look up details via tools.

3. **Context vs instructions**: Use `WithInstructions` for permanent behavioral rules. Use the context dictionary for per-request, per-user, or per-session data that varies.

4. **Ordering matters**: The "Runtime context:" section appears at the end of the user message. Instructions in the system prompt that reference it should be near the start of `WithInstructions` so the model processes them early.

5. **Don't put secrets in context**: Context values are sent to the LLM provider. Never include API keys, passwords, or tokens.

6. **Verify with tracing**: Enable `PromptTracing` and check the inspector to confirm your context keys are appearing as expected.