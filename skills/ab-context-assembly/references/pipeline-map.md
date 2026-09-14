# Context Assembly Pipeline — End-to-End Walkthrough

How AgentBlazor assembles the full LLM context for every agent turn, from the consumer's perspective. Each section traces one piece of context from its consumer-facing API to where it lands in the LLM input.

## Table of contents

- [Context Assembly Pipeline — End-to-End Walkthrough](#context-assembly-pipeline--end-to-end-walkthrough)
  - [Table of contents](#table-of-contents)
  - [System instructions](#system-instructions)
    - [Source](#source)
    - [Also contributes: `WithDescription(string)`](#also-contributes-withdescriptionstring)
  - [Data schemas](#data-schemas)
    - [Source](#source-1)
    - [What happens](#what-happens)
    - [Key contract](#key-contract)
  - [Tool definitions](#tool-definitions)
    - [Source](#source-2)
    - [Where they go](#where-they-go)
  - [User message + runtime context](#user-message--runtime-context)
    - [User input](#user-input)
    - [Runtime context injection](#runtime-context-injection)
    - [Generated UI context](#generated-ui-context)
  - [Conversation history](#conversation-history)
    - [Storage](#storage)
    - [Control](#control)
  - [Middleware position](#middleware-position)
  - [Summary table](#summary-table)

---

## System instructions

### Source

The primary system prompt is set once at agent registration time via `AgentRegistrationBuilder.WithInstructions(string)`:

```csharp
builder.AddAgent("support", agent =>
{
    agent.WithInstructions("You are a helpful support agent. Always verify the ticket ID before acting.");
});
```

This string becomes the agent's system instructions — the equivalent of the "system prompt" in chat-completion APIs. The package sends it as-is to the LLM provider.

### Also contributes: `WithDescription(string)`

The description set via `WithDescription()` is passed as agent metadata (not system instructions). It may be used internally for agent selection and routing but does **not** appear in the system prompt.

---

## Data schemas

### Source

Per-agent opt-in via `AgentRegistrationBuilder.WithDataSchemas(params string[])`:

```csharp
builder.AddAgent("support", agent =>
{
    agent.WithDataSchemas("support-data", "billing-data");
});
```

### What happens

When an agent lists one or more data schemas, the package automatically appends formatted schema documentation to the system instructions. The consumer's `WithInstructions` text comes first, followed by a section like:

```
# READ-SAFE DATA SCHEMAS
The following entity schemas are exposed for planning context only...

- Schema: support-data - ...
  - Entity: support_tickets - ...
    - Id: string, key
    - Subject: string - ...
```

Schemas are registered globally via `builder.AddDataSchema(schemaSet)` or `builder.AddDataSchema(factory)`.

### Key contract

- Only schemas listed in `WithDataSchemas(...)` are exposed to the agent — no other schemas leak.
- The string arguments to `WithDataSchemas(...)` must match the `Name` property of a registered schema set exactly. There is no wildcard or prefix matching.
- If `WithDataSchemas` is not called for an agent, no schema text is appended.

---

## Tool definitions

### Source

Tools (service tools, capability actions, component actions) are registered via `options.AddTool(...)` or by adding capabilities with `[AgentAction]` methods.

### Where they go

Tool descriptions are sent as **native function-calling tool definitions**, NOT embedded in the system prompt. The LLM receives them as structured function declarations with names, descriptions, and parameter schemas.

This means:
- The system prompt should describe **who the agent is** and **how to behave**.
- Tool descriptions should describe **what each tool does** and **what parameters it needs**.
- They are separate channels. Don't duplicate tool descriptions into `WithInstructions`.

---

## User message + runtime context

### User input

The raw text typed in the chat composer becomes the user message sent to the LLM.

### Runtime context injection

The package automatically appends context dictionary entries to the user message. The **known format** is:

```
[user's typed message]

Runtime context:
- agentblazor.current_route: /tickets/123
- agentblazor.agent_name: support
- agentblazor.agent_lock: True
- myapp.user_role: admin
```

Keys are sorted alphabetically. Each line follows `- key: value` format.

The context dictionary is populated from two sources:

1. **Chat components** (`AgentChatSurface`, `AgentChatWidget`, `AgentChatBar`) automatically set:
   - `AgentRuntimeContextKeys.CurrentRoute` — the current browser URL path
   - `AgentRuntimeContextKeys.AgentName` + `AgentLock` — when an agent is locked/selected
   - Handoff keys (`AgentHandoffFrom`, `AgentHandoffTo`, `AgentHandoffAt`) — during agent handoff
   - `GenerateUiContextKey` — when generative UI is enabled

2. **Your middleware** can add custom keys to `context.Request.Context` (see [context dictionary](context-dictionary.md)).

### Generated UI context

When a user interacts with a generated-UI card, the action metadata (BlockId, ActionId, Prompt, Payload) is appended to the user message before the runtime context section:

```
[user's typed message]

Generated UI action context:
BlockId: card_123
ActionId: refresh
Prompt: Show updated list
Payload: {"filter":"active"}

Runtime context:
- ...
```

---

## Conversation history

### Storage

Conversation history is stored via `IConversationStore`. The package manages two aspects:

1. **Persistence**: Each turn (user message + agent response + execution data) is saved to the store. Configure with `builder.UseConversationStore<TStore>()` or `builder.UseJsonFileConversationStore(path)`.

2. **Live session**: The package maintains the live turn-by-turn session state internally. The LLM receives recent conversation turns automatically as part of its context.

### Control

- `ConversationOptions.MaxHistoryInPrompt` (default: 5) — limits how many recent turns are included in each LLM request.
- `ConversationOptions.MaxTurnsPerSession` (default: 50) — caps total turns per session.
- `ConversationOptions.SessionTimeout` (default: 24h) — sessions expire after inactivity.
- `ConversationOptions.IncludeActionResultsInHistory` (default: `true`) — whether tool execution results are included in the history sent to the LLM.

These are configured via `builder.UseConversationStore<TStore>(configure)` or `options.ConfigureBuilder(...)`.

---

## Middleware position

Middleware registered via `options.UseMiddleware(...)` or `builder.UseMiddleware<T>()` wraps the entire turn execution. The pipeline runs in registration order, **before** the LLM is invoked.

Middleware can:
- Read and modify `context.Request.Context` (the context dictionary) to inject runtime data
- Set `context.Response` to short-circuit the turn (skip the LLM entirely)
- Use `context.Items` as a scratchpad to pass data between middleware layers

Middleware **cannot** directly modify the system instructions (those are set at registration time). For dynamic instructions, see [dynamic instructions](dynamic-instructions.md).

---

## Summary table

| Context piece | Consumer API | Lands in |
|---|---|---|
| Agent instructions | `WithInstructions(string)` | System prompt |
| Data schema docs | `WithDataSchemas(...)` → `AddDataSchema(...)` | System prompt (appended) |
| Tool/capability descriptions | `AddTool(...)`, `[AgentAction]` | Native function-calling tool definitions |
| User message | Chat composer input | User message |
| Runtime context | `AgentRuntimeContextKeys` + middleware | User message (`- key: value` under "Runtime context:") |
| Generated UI actions | Chat component `EnableGeneratedUi` | User message (action metadata block) |
| Conversation history | `IConversationStore` + internal session | LLM context (auto-managed) |
| Agent description | `WithDescription(string)` | Agent metadata (routing/selection only) |