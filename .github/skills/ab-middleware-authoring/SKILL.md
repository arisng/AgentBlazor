---
name: ab-middleware-authoring
description: "Author custom middleware in the AgentBlazor agent turn pipeline. Use when implementing IAgentTurnMiddleware, building cross-cutting concerns (logging, cost control, tenant enrichment, audit, rate limiting), working with AgentTurnContext, short-circuiting turns, registering inline delegates or typed middlewares, and understanding execution order and scope resolution. Triggers: IAgentTurnMiddleware, AgentTurnContext, UseMiddleware, AgentMiddlewarePipeline, IAgentExecutionScopeAccessor, short-circuit, middleware pipeline, agent turn middleware."
---

# `ab-middleware-authoring` — Middleware Authoring

## Overview

Middleware in AgentBlazor wraps every agent turn (LLM request→response) in an onion chain. Each middleware can inspect, modify, log, or short-circuit the turn.

**This is NOT ASP.NET Core middleware.** It runs inside the AgentBlazor runtime, inside each turn — not at the HTTP level.

```
Incoming request (user message)
  │
  ▼
Middleware 1 (outermost — registered first)
  │
  ▼
Middleware 2
  │
  ▼
Middleware 3 (innermost)
  │
  ▼
Runtime calls LLM → gets response
  │
  ▼ (response flows back out)
Middleware 3 → Middleware 2 → Middleware 1
  │
  ▼
Response to user
```

## `IAgentTurnMiddleware` Interface

```csharp
namespace AgentBlazor.Core.Runtime.Middleware;

public interface IAgentTurnMiddleware
{
    Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct = default);
}
```

## `AgentTurnContext`

```csharp
public sealed class AgentTurnContext
{
    public AgentTurnContext(AgentTurnRequest request);

    public AgentTurnRequest Request { get; }       // The incoming turn request (message, agent name, etc.)
    public AgentTurnResponse? Response { get; set; } // Set to short-circuit — skips next() and LLM
    public IDictionary<string, object?> Items { get; }  // Key-value bag for cross-middleware data
    public bool IsShortCircuited => Response is not null;
}
```

## Two Registration Styles

Both are called on `AgentBlazorRegistrationOptions`:

### 1. Inline Delegate

```csharp
options.UseMiddleware(async (ctx, next, ct) =>
{
    // Before: read/modify request
    ctx.Items["startedAt"] = DateTime.UtcNow;

    await next(ct);  // Call the next middleware (or the LLM)

    // After: read/modify response
    var duration = DateTime.UtcNow - (DateTime)ctx.Items["startedAt"]!;
    LogTurnDuration(duration);
});
```

### 2. Typed Middleware (resolved from DI)

```csharp
public sealed class TenantCostControlMiddleware : IAgentTurnMiddleware
{
    private readonly ITenantCostStore _costStore;
    private readonly ILogger _logger;

    public TenantCostControlMiddleware(
        ITenantCostStore costStore,          // resolved from DI
        ILogger<TenantCostControlMiddleware> logger)
    {
        _costStore = costStore;
        _logger = logger;
    }

    public async Task InvokeAsync(AgentTurnContext ctx, Func<CancellationToken, Task> next, CancellationToken ct)
    {
        // Check budget before allowing the turn
        var tenantId = ctx.Items.TryGetValue("TenantId", out var id) ? id?.ToString() : null;
        if (tenantId is not null)
        {
            var budget = await _costStore.GetDailySpendAsync(tenantId, DateTime.UtcNow.Date, ct);
            if (budget >= 50.00m)
            {
                ctx.Response = new AgentTurnResponse
                {
                    Text = "Daily budget exceeded for this tenant.",
                    Status = AgentTurnStatus.Blocked
                };
                return; // short-circuit — never calls next()
            }
        }

        await next(ct);  // allow the turn
    }
}

// Registration:
options.UseMiddleware<TenantCostControlMiddleware>();
```

## Short-Circuiting

Set `context.Response` to skip the LLM call entirely:

```csharp
options.UseMiddleware(async (ctx, next, ct) =>
{
    if (ctx.Items.ContainsKey("blocked"))
    {
        ctx.Response = new AgentTurnResponse
        {
            Text = "Cannot process this request.",
            Status = AgentTurnStatus.Blocked
        };
        return; // ← does NOT call next()
    }
    await next(ct);
});
```

Check `ctx.IsShortCircuited` to detect if an outer middleware already short-circuited.

## Cross-Middleware Data Sharing

Use `ctx.Items` dictionary:

```csharp
// Middleware 1: Enrich
options.UseMiddleware(async (ctx, next, ct) =>
{
    ctx.Items["TenantId"] = ResolveTenantId();
    await next(ct);
});

// Middleware 2: Consume
options.UseMiddleware(async (ctx, next, ct) =>
{
    var tenantId = ctx.Items["TenantId"];
    Log.Audit(tenantId, ctx.Request);
    await next(ct);
});
```

## Execution Order

Middlewares execute in **registration order** (first registered = outermost):

```csharp
options.UseMiddleware<A>();  // outer — runs first, calls next
options.UseMiddleware<B>();  // inner — runs after A, calls next
                             // LLM runs after B
                             // Response flows back: B → A
```

## DI Resolution

Typed middlewares are registered as transient in DI automatically by `UseMiddleware<T>()`. At runtime, they are resolved via `IAgentExecutionScopeAccessor` — which uses `AsyncLocal<IServiceProvider?>` to enable proper scoped service resolution in async flows:

```csharp
// Inside the middleware pipeline, when resolving typed middlewares:
var serviceProvider = scopeAccessor.Current ?? sp;  // falls back to root scope
var middleware = (IAgentTurnMiddleware)serviceProvider.GetRequiredService(middlewareType);
return middleware.InvokeAsync(ctx, next, ct);
```

This means typed middlewares can access **scoped** services (e.g., DbContext, tenant context) even though the pipeline is built as a singleton.

> **Scope boundary:** the pushed execution scope lives only inside `using (ExecutionScopeAccessor.Push(...))` — in the FSH surface it wraps only `RunTurnWithOptionalStreamingAsync`. Anything that resolves a store or context after the push block disposes (UI lifecycle, the conversation-store rewrite path) sees a fresh DI scope with empty scoped services and MUST use a scope-bridging cache, never ambient scoped state. See [Scope-boundary execution](references/scope-boundary-execution.md).

## Scripts/Reference

When building a middleware that needs to query cost or budget stores, see the cost-control patterns described in the multi-tenant blueprint (`docs/multi-tenant-production-blueprint.md`).

## Reference files

- [Scope-boundary execution](references/scope-boundary-execution.md) — IN vs OUTSIDE the agent execution scope, and the rule that outside-scope store/context resolution must use a scope-bridging cache

## Complete Example — Tenant Enrichment + Audit Middleware

```csharp
// Middleware 1: Resolve tenant and inject into Items
public sealed class TenantEnrichmentMiddleware : IAgentTurnMiddleware
{
    private readonly ITenantContextAccessor _accessor;
    public TenantEnrichmentMiddleware(ITenantContextAccessor accessor) => _accessor = accessor;

    public async Task InvokeAsync(AgentTurnContext ctx, Func<CancellationToken, Task> next, CancellationToken ct)
    {
        var tenant = _accessor.TenantContext;
        if (tenant is not null)
        {
            ctx.Items["TenantId"] = tenant.TenantId;
            ctx.Items["TenantPlan"] = tenant.Plan.ToString();
        }
        await next(ct);
    }
}

// Middleware 2: Audit log every turn
public sealed class AuditLogMiddleware : IAgentTurnMiddleware
{
    private readonly IAuditLog _log;
    public AuditLogMiddleware(IAuditLog log) => _log = log;

    public async Task InvokeAsync(AgentTurnContext ctx, Func<CancellationToken, Task> next, CancellationToken ct)
    {
        await next(ct);  // let the turn complete

        if (ctx.Response is not null)
        {
            await _log.RecordAsync(new AuditEntry(
                TenantId: ctx.Items["TenantId"]?.ToString(),
                RequestId: ctx.Request.RequestId,
                Succeeded: ctx.Response.Status == AgentTurnStatus.Success,
                TimestampUtc: DateTime.UtcNow
            ), ct);
        }
    }
}

// Registration (order matters):
options.UseMiddleware<TenantEnrichmentMiddleware>();  // runs first
options.UseMiddleware<AuditLogMiddleware>();           // runs second
```

## Usage Recording

Reference implementation: `UsageRecordingMiddleware` in `Playground.Lifeline/Services/` (registered innermost, only when `AgentChat:UseTenantStore=true`). Contract:

- **Capture after `await next(ct)`** — the run-execution scope is only live once the turn completes, so the usage read must happen strictly after `next()`.
- **Null-usage no-op** — early exits and short-circuits leave `ctx.Response.Usage` null; skip recording when there is no usage or no effective conversation id (`"global"` / whitespace).
- **ConversationId** — `ctx.Request.GetEffectiveSessionId()` yields the wire key to attribute the record to.
- **TurnSequence idempotency key** — source is the context `agentblazor.run_id` when present, else a fresh `"N"`-format GUID stashed in `ctx.Items["TurnSequence"]` before `next()` so retries within one invocation dedupe on the API's unique `(ConversationId, TurnSequence)` index.
- **Transport** — POST via an auth-wired `HttpClient` resolved from `IAgentExecutionScopeAccessor.Current` (circuit scope), with an `IServiceScopeFactory` fallback for HTTP-endpoint paths.
- **Never throw** — the whole capture/POST is wrapped in try/catch; failures are logged as warnings, never propagated into the turn.
- **Store-mode gate** — register the middleware only when the tenant-backed conversation store is enabled (usage persistence goes through the module API in that mode).
