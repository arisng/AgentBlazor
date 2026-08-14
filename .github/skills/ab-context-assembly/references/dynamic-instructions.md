# Dynamic Instructions — Runtime Instruction Customization

`AgentRegistrationBuilder.WithInstructions(string)` sets a static system prompt at registration time. This document covers approaches for injecting dynamic, per-request data when the static string isn't enough — all using the public API surface.

## Table of contents

1. [The constraint](#the-constraint)
2. [Approach 1: Context dictionary injection](#approach-1-context-dictionary-injection)
3. [Approach 2: Middleware enrichment](#approach-2-middleware-enrichment)
4. [Approach 3: Replace IAgentRuntimeAdapter](#approach-3-replace-iagentruntimeadapter)
5. [Approach 4: Middleware short-circuit](#approach-4-middleware-short-circuit)
6. [Comparison](#comparison)

---

## The constraint

`WithInstructions(string)` accepts a static string. There is no factory/delegate overload. If you need instructions that vary per user, per tenant, or per time of day, you need one of the approaches below.

---

## Approach 1: Context dictionary injection

**Power**: Low &nbsp;|&nbsp; **Complexity**: Low &nbsp;|&nbsp; **When**: Simplest cases

Inject dynamic data into the user message rather than the system prompt. Pair it with instructions that explicitly direct the agent to read the "Runtime context:" section.

### How it works

The package appends context dictionary entries to the user message as `- key: value` lines under a "Runtime context:" heading. You add custom keys via middleware.

### Example

**Instructions** (static):
```csharp
agent.WithInstructions("""
    You are a support agent. At the start of every turn, check the
    "Runtime context:" section in the user message for the current
    user's role and timezone. Restrict actions accordingly.
    """);
```

**Middleware** (per-request):
```csharp
public class UserContextEnrichmentMiddleware : IAgentTurnMiddleware
{
    private readonly IUserContext _userContext;

    public UserContextEnrichmentMiddleware(IUserContext userContext)
    {
        _userContext = userContext;
    }

    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct)
    {
        context.Request.Context ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        context.Request.Context["myapp.user_role"] = _userContext.CurrentRole;
        context.Request.Context["myapp.timezone"] = _userContext.Timezone;
        await next(ct);
    }
}
```

### Limitations

- Data appears in the **user message**, not the system prompt. The model may weight it differently.
- You must teach the agent (via `WithInstructions`) to look for and obey the runtime context.

---

## Approach 2: Middleware enrichment

**Power**: Medium &nbsp;|&nbsp; **Complexity**: Medium &nbsp;|&nbsp; **When**: You need per-request data in the turn but don't need to modify system instructions directly

Middleware runs before every turn and can:

- Add entries to `context.Request.Context` (shown in the user message)
- Store data in `context.Items` dictionary (for cross-middleware communication)
- Set `context.Response` to short-circuit (see [Approach 4](#approach-4-middleware-short-circuit))

### Pattern: tenant-aware enrichment

```csharp
public class TenantContextMiddleware : IAgentTurnMiddleware
{
    private readonly ITenantContext _tenant;

    public TenantContextMiddleware(ITenantContext tenant)
    {
        _tenant = tenant;
    }

    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct)
    {
        context.Request.Context ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        context.Request.Context["tenant.id"] = _tenant.CurrentId;
        context.Request.Context["tenant.tier"] = _tenant.SubscriptionTier;

        // Pass computed data to downstream middleware
        context.Items["TenantBudget"] = await _tenant.GetBudgetAsync(ct);

        await next(ct);
    }
}
```

### Limitations

- Cannot modify the system instructions text.
- Cannot modify tool definitions.
- Context dict entries only influence the model if the instructions tell it to read them.

---

## Approach 3: Replace `IAgentRuntimeAdapter`

**Power**: High &nbsp;|&nbsp; **Complexity**: High &nbsp;|&nbsp; **When**: You need full control over how the prompt is built

Replace the entire runtime adapter. This gives you full access to the `AgentTurnRequest` and lets you build the prompt however you want.

### Interface

```csharp
public interface IAgentRuntimeAdapter
{
    bool SupportsStreaming { get; }
    bool SupportsReconnect { get; }
    bool SupportsCancellation { get; }

    Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
        string runId,
        CancellationToken cancellationToken = default);

    Task<bool> StopRunAsync(
        string runId,
        CancellationToken cancellationToken = default);
}
```

### Registration

```csharp
// Typed replacement
builder.UseRuntimeAdapter<MyCustomRuntimeAdapter>();

// Factory-backed replacement (for DI-heavy adapters)
builder.UseRuntimeAdapter(sp => new MyCustomRuntimeAdapter(
    sp.GetRequiredService<IChatClient>(),
    sp.GetRequiredService<IUserContext>()
));
```

### Decorator pattern (preserve default behavior, add custom logic)

Wrap the default adapter if you only need to modify instructions:

```csharp
public class InstructionDecoratorAdapter(
    IAgentRuntimeAdapter _inner,
    IUserContext _userContext) : IAgentRuntimeAdapter
{
    public bool SupportsStreaming => _inner.SupportsStreaming;
    public bool SupportsReconnect => _inner.SupportsReconnect;
    public bool SupportsCancellation => _inner.SupportsCancellation;

    public Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken ct = default)
    {
        // Inject custom data into the context dictionary before delegating
        request.Context ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        request.Context["custom.instructions"] = BuildDynamicInstructions(_userContext);
        return _inner.RunTurnAsync(request, ct);
    }

    // ... delegate other methods similarly
}
```

### Limitations

- You take responsibility for the entire turn lifecycle.
- Must handle streaming, cancellation, and reconnection if you support those.
- Testing surface is large.

---

## Approach 4: Middleware short-circuit

**Power**: High &nbsp;|&nbsp; **Complexity**: Medium-High &nbsp;|&nbsp; **When**: You want to bypass the default pipeline entirely for specific conditions

Set `context.Response` in middleware to skip the normal LLM invocation and return a custom response directly.

### Example: daily cost limit

```csharp
public class CostLimitMiddleware : IAgentTurnMiddleware
{
    private readonly ICostTracker _costTracker;

    public CostLimitMiddleware(ICostTracker costTracker)
    {
        _costTracker = costTracker;
    }

    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct)
    {
        if (await _costTracker.IsDailyLimitExceededAsync(ct))
        {
            context.Response = new AgentTurnResponse(
                context.Request.AgentName ?? "Agent",
                "Daily usage limit reached. Please try again tomorrow.",
                [],
                []);
            return; // short-circuit — next() is never called
        }

        await next(ct);
    }
}
```

### Limitations

- You bypass the LLM entirely — the response is fully hand-crafted.
- You can't modify the prompt and still call the LLM. If you need both, use [Approach 3](#approach-3-replace-iagentruntimeadapter).

---

## Comparison

| Approach | Dynamic instructions | Dynamic context | Full prompt control | Complexity |
|---|---|---|---|---|
| 1. Context dictionary | ⚠️ Indirect | ✅ Yes | ❌ No | Low |
| 2. Middleware enrichment | ⚠️ Indirect | ✅ Yes | ❌ No | Medium |
| 3. Replace adapter | ✅ Yes | ✅ Yes | ✅ Yes | High |
| 4. Middleware short-circuit | ❌ Bypass | ❌ Bypass | ✅ Custom only | Medium-High |

> **Rule of thumb**: Start with approach 1 or 2. Graduate to approach 3 only when you need to restructure the system prompt itself per request. Use approach 4 for gating (auth, rate limits, cost caps) — not for prompt customization.