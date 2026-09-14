# AgentBlazor Middleware Stack for Multi-Tenancy

## Contents

- [Execution Order](#execution-order)
- [Tenant Enrichment Middleware](#tenant-enrichment-middleware)
- [Cost Control Middleware](#cost-control-middleware)
  - [Cost Store Backend Choices](#cost-store-backend-choices)
- [Audit Logging Middleware](#audit-logging-middleware)
- [Per-Tenant Options Resolution](#per-tenant-options-resolution)

`IAgentTurnMiddleware` runs inside every agent turn — the ideal place for cost control, audit logging, and tenant enrichment.

## Execution Order

```csharp
builder.Services.AddAgentBlazor(options =>
{
    // Outermost (runs first) — enrich context
    options.UseMiddleware<TenantEnrichmentMiddleware>();

    // Budget enforcement before LLM call
    options.UseMiddleware<TenantCostControlMiddleware>();

    // Logging after response
    options.UseMiddleware<TenantAuditLoggingMiddleware>();
});
```

```
Incoming turn
  │
  ▼
TenantEnrichmentMiddleware       ← sets TenantId in AgentTurnContext.Items
  │
  ▼
TenantCostControlMiddleware      ← checks budget, short-circuits if exceeded
  │
  ▼
LLM call (TenantAwareChatClient)
  │
  ▼ (response)
TenantAuditLoggingMiddleware     ← records token usage, cost, outcome
  │
  ▼
Response to user
```

## Tenant Enrichment Middleware

Injects tenant identity into `AgentTurnContext.Items` so downstream middleware can read it:

```csharp
public sealed class TenantEnrichmentMiddleware : IAgentTurnMiddleware
{
    private readonly TenantContextAccessor _tenantAccessor;

    public TenantEnrichmentMiddleware(TenantContextAccessor tenantAccessor)
        => _tenantAccessor = tenantAccessor;

    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct)
    {
        var tenant = _tenantAccessor.TenantContext;
        if (tenant is not null)
        {
            context.Items["TenantId"] = tenant.TenantId;
            context.Items["TenantPlan"] = tenant.Tier;
            context.Items["TenantProvider"] = tenant.ProviderType;
        }
        await next(ct);
    }
}
```

## Cost Control Middleware

> **Important:** `AgentTurnContext.Items["TokenUsage"]` is NOT populated by AgentBlazor automatically. You must add a separate `IAgentTurnMiddleware` (or wrap the `IChatClient`) to capture token usage from the LLM response and store it in `context.Items["TokenUsage"]` as a `TokenUsage` instance before the cost control middleware runs. Register the token-capture middleware *before* the cost control middleware.

Checks daily/monthly budgets before the LLM call. Short-circuits if exceeded:

```csharp
public sealed class TenantCostControlMiddleware : IAgentTurnMiddleware
{
    private readonly TenantContextAccessor _tenantAccessor;
    private readonly ITenantCostStore _costStore;
    private readonly ILogger<TenantCostControlMiddleware> _logger;

    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct)
    {
        var tenant = _tenantAccessor.TenantContext;
        if (tenant is null) { await next(ct); return; }

        var dailySpend = await _costStore.GetDailySpendAsync(tenant.TenantId, DateTime.UtcNow.Date, ct);
        if (dailySpend >= tenant.DailyBudgetUsd)
        {
            context.Response = new AgentTurnResponse
            {
                Text = $"Daily budget of ${tenant.DailyBudgetUsd:F2} reached for {tenant.TenantName}. Try again tomorrow.",
                Status = AgentTurnStatus.Blocked
            };
            return; // short-circuit
        }

        var monthlySpend = await _costStore.GetMonthlySpendAsync(tenant.TenantId, DateTime.UtcNow, ct);
        if (tenant.HardCapEnabled && monthlySpend >= tenant.MonthlyBudgetUsd)
        {
            context.Response = new AgentTurnResponse
            {
                Text = $"Monthly budget of ${tenant.MonthlyBudgetUsd:F2} reached for {tenant.TenantName}.",
                Status = AgentTurnStatus.Blocked
            };
            return;
        }

        await next(ct);

        // After response, record cost
        if (context.Items.TryGetValue("TokenUsage", out var usage) && usage is TokenUsage tu)
        {
            var cost = tu.PromptTokens * tenant.InputTokenCostPerMillion / 1_000_000m
                     + tu.CompletionTokens * tenant.OutputTokenCostPerMillion / 1_000_000m;
            await _costStore.RecordUsageAsync(tenant.TenantId, new CostRecord
            {
                TenantId = tenant.TenantId,
                PromptTokens = tu.PromptTokens,
                CompletionTokens = tu.CompletionTokens,
                EstimatedCostUsd = cost,
                TimestampUtc = DateTime.UtcNow
            }, ct);
        }
    }
}

// Simplified cost store interface
public interface ITenantCostStore
{
    Task<decimal> GetDailySpendAsync(string tenantId, DateTime date, CancellationToken ct);
    Task<decimal> GetMonthlySpendAsync(string tenantId, DateTime month, CancellationToken ct);
    Task RecordUsageAsync(string tenantId, CostRecord record, CancellationToken ct);
}
```

### Cost Store Backend Choices

| Backend | Pros | Cons |
|---|---|---|
| **SQL Server** (per-tenant DB) | Durable, queryable, co-located with tenant data | Extra DB writes per turn |
| **Redis** | Fast counters, TTL-based auto-expiry | Needs separate Redis instance |
| **Hybrid** (Redis counters + SQL audit) | Real-time enforcement + durable audit | Two infra dependencies |

**Redis implementation for fast budget counters:**

```csharp
public async Task RecordUsageAsync(string tenantId, CostRecord record, CancellationToken ct)
{
    var dateKey = $"cost:daily:{tenantId}:{record.TimestampUtc:yyyy-MM-dd}";
    var monthKey = $"cost:monthly:{tenantId}:{record.TimestampUtc:yyyy-MM}";

    var tx = _redis.CreateTransaction();
    _ = tx.StringIncrementAsync(dateKey, (double)record.EstimatedCostUsd);
    _ = tx.KeyExpireAsync(dateKey, TimeSpan.FromDays(90));
    _ = tx.StringIncrementAsync(monthKey, (double)record.EstimatedCostUsd);
    _ = tx.KeyExpireAsync(monthKey, TimeSpan.FromDays(180));
    await tx.ExecuteAsync();
}
```

## Audit Logging Middleware

Records every turn for compliance with tenant context:

```csharp
public sealed class TenantAuditLoggingMiddleware : IAgentTurnMiddleware
{
    private readonly TenantContextAccessor _tenantAccessor;
    private readonly ILogger<TenantAuditLoggingMiddleware> _logger;

    public TenantAuditLoggingMiddleware(
        TenantContextAccessor tenantAccessor,
        ILogger<TenantAuditLoggingMiddleware> logger)
    {
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct)
    {
        var tenantId = _tenantAccessor.TenantContext?.TenantId ?? "anonymous";
        var startedAt = DateTimeOffset.UtcNow;

        await next(ct);

        _logger.LogInformation(
            "Agent turn: Tenant={TenantId}, Agent={AgentName}, Duration={Duration}ms, Status={Status}",
            tenantId,
            context.Request.AgentName,
            (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            context.Response?.Status);
    }
}
```

## Per-Tenant Options Resolution

Middleware can resolve tenant-specific `ConversationOptions` at runtime:

```csharp
public async Task InvokeAsync(
    AgentTurnContext context,
    Func<CancellationToken, Task> next,
    CancellationToken ct)
{
    var tenant = _tenantAccessor.TenantContext;
    var maxTurns = tenant?.MaxTurnsPerSession ?? 50;   // from TenantInfo
    var maxHistory = tenant?.MaxHistoryInPrompt ?? 5;
    var timeout = tenant?.SessionTimeout ?? TimeSpan.FromHours(24);

    context.Items["TenantMaxTurns"] = maxTurns;
    context.Items["TenantMaxHistory"] = maxHistory;
    // Pass to conversation store or trim logic

    await next(ct);
}
```
