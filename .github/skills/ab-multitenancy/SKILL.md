---
name: ab-multitenancy
description: "Enable multi-tenant production deployments of AgentBlazor apps on Blazor Interactive Server with BFF + API + SQL Server. Covers tenant resolution with Finbuckle.MultiTenant, per-tenant LLM provider via proxy IChatClient, per-tenant EF Core conversation/data stores, AgentBlazor middleware for cost control and tenant enrichment, and the full BFF integration pattern. Use when asked about multi-tenant AgentBlazor, per-tenant AI providers, tenant isolation in agent conversations, Finbuckle setup with AgentBlazor, or productionizing AgentBlazor for SaaS platforms. Triggers: multi-tenant, multitenant, SaaS, tenant isolation, per-tenant, Finbuckle, BFF pattern, tenant context, TenantAwareChatClient, proxy IChatClient, tenant resolution, ITenantContext, AgentBlazor multi-tenant, ab-multitenancy."
metadata:
    version: 0.1.0
---

# Multi-Tenant AgentBlazor — Finbuckle Edition

Guide for enabling production multi-tenancy in AgentBlazor on a Blazor Interactive Server host, using [Finbuckle.MultiTenant](https://www.finbuckle.com) for tenant resolution, per-tenant SQL Server databases, and per-tenant LLM providers.

## Prerequisites

```xml
<PackageVersion Include="Finbuckle.MultiTenant" Version="10.0.4" />
<PackageVersion Include="Finbuckle.MultiTenant.AspNetCore" Version="10.0.4" />
<PackageVersion Include="Finbuckle.MultiTenant.EntityFrameworkCore" Version="10.0.4" />
<PackageVersion Include="Finbuckle.MultiTenant.Abstractions" Version="10.0.4" />
```

Target: Blazor Interactive Server (.NET 10+), SQL Server, EF Core.

## CLI Baseline First (Existing Solutions)

If you are onboarding an **existing** `.sln`/`.slnx` Blazor solution (not greenfield), run the `agentblazor` CLI to lay down the baseline AgentBlazor wiring *before* the Finbuckle + proxy `IChatClient` steps below. See the **`ab-cli` skill** for:

- `agentblazor init` → create `.agentblazor/AGENT.md`
- `agentblazor analyze --scan-scope solution` → read-only report of the existing structure (note: `*Provider`/infrastructure — including tenant-resolution — is filtered out as non-agent-relevant; translate tenant assets manually)
- `agentblazor scaffold --provider openai --diff` → preview, then `--approve` → apply the non-provider wiring (imports, Mud services, endpoints, shell assets, chat surface)
- Then return here: replace the scaffolded direct-`UseOpenAI` block with the proxy `IChatClient` + `TenantAwareChatClient` (Step 6 below), and add the Finbuckle wiring + per-tenant store + middleware from Steps 1–7.

The CLI is **non-destructive** for the `AddAgentBlazor(...)` registration — it only inserts when `AddAgentBlazor(` is absent — so re-running `scaffold` after the proxy swap is safe and inert.

## Architecture

```
User Browser (SignalR circuit)
  │
  ▼
BFF Host ─── HTTP (REST) ───► API App
  │                              │
  ├─ TenantResolutionMiddleware  ├─ Finbuckle tenant resolution
  ├─ AgentBlazor Runtime         ├─ Per-tenant DbContext
  │  ├─ TenantAwareChatClient    ├─ Per-tenant SQL Server DB
  │  ├─ ConversationStore        └─ Business logic controllers
  │  └─ IAgentTurnMiddleware
  └─ Blazor UI (AgentChatSurface)
```

## Core Principle: AsyncLocal Tenant + Proxy IChatClient

AgentBlazor registers `IChatClient` as a **singleton** in DI. You cannot create per-tenant `ChatClientRuntimeAdapter` instances — the adapter stores `_chatClient` as `readonly` at construction time and never re-resolves it.

**Correct pattern:** Register a **singleton proxy `IChatClient`** that resolves the real provider per-call, via an `AsyncLocal`-backed tenant accessor.

```
TenantResolutionMiddleware (ASP.NET Core)
  └─ Sets TenantContextAccessor.TenantContext (AsyncLocal)

AgentChatSurface → RunTurnAsync()
  └─ ChatClientRuntimeAdapter → _chatClient.CompleteAsync()
       └─ TenantAwareChatClient (proxy)
            ├─ Reads tenant from AsyncLocal accessor
            └─ Delegates to per-tenant OpenAI/Azure/Ollama client
```

## Quick Start

### Step 1 — Define Tenant Info Model

```csharp
// Shared/ITenantContext.cs
public interface ITenantContext
{
    string TenantId { get; }
    string? TenantName { get; }
    string ProviderType { get; }     // "OpenAI", "AzureOpenAI", "Ollama"
    string? ApiKey { get; }
    string? Endpoint { get; }
    string Model { get; }
    string? AzureDeploymentName { get; }
    string ConnectionString { get; }
    decimal DailyBudgetUsd { get; }
    decimal MonthlyBudgetUsd { get; }
}
```

### Step 2 — AsyncLocal Tenant Accessor

```csharp
// Bff/Services/TenantContextAccessor.cs
public sealed class TenantContextAccessor
{
    private static readonly AsyncLocal<ITenantContext?> _current = new();

    public ITenantContext? TenantContext
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
```

Register as **singleton** — it must be resolvable from the root container and from scoped containers alike:

```csharp
builder.Services.AddSingleton<TenantContextAccessor>();
```

### Step 3 — Proxy IChatClient

```csharp
// Bff/Services/TenantAwareChatClient.cs
public sealed class TenantAwareChatClient : IChatClient
{
    private readonly TenantContextAccessor _tenantAccessor;
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new();

    public TenantAwareChatClient(TenantContextAccessor tenantAccessor)
        => _tenantAccessor = tenantAccessor;

    public async Task<ChatCompletion> CompleteAsync(
        IList<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var tenant = _tenantAccessor.TenantContext
            ?? throw new InvalidOperationException("No tenant context set.");
        var client = _clients.GetOrAdd(tenant.TenantId, _ => CreateClient(tenant));
        return await client.CompleteAsync(messages, options, ct);
    }

    // Also implement CompleteStreamingAsync, GetService<TService>, get Metadata

    private IChatClient CreateClient(ITenantContext tenant) => tenant.ProviderType switch
    {
        "OpenAI" => new OpenAIClient(tenant.ApiKey!).GetChatClient(tenant.Model).AsIChatClient(),
        "AzureOpenAI" => new AzureOpenAIClient(
            new Uri(tenant.Endpoint!),
            new ApiKeyCredential(tenant.ApiKey!))
            .GetChatClient(tenant.AzureDeploymentName ?? tenant.Model).AsIChatClient(),
        _ => throw new NotSupportedException($"Provider '{tenant.ProviderType}' not supported.")
    };
}
```

### Step 4 — Finbuckle Tenant Resolution

```csharp
// Program.cs
builder.Services.AddMultiTenant<TenantInfo>()
    .WithResolutionStrategy<HostResolutionStrategy>()     // {tenant}.yourapp.com
    .WithResolutionStrategy<HeaderResolutionStrategy>()   // X-Tenant-Id
    .WithResolutionStrategy<CookieResolutionStrategy>()   // TenantId cookie
    .WithStore<EFCoreStore<TenantDbContext, TenantInfo>>(ServiceLifetime.Scoped)
    .WithPerTenantConnectionString(options =>
    {
        options.DefaultConnectionString = builder.Configuration.GetConnectionString("Shared")!;
    });

// TenantInfo must implement ITenantInfo (Finbuckle) and ITenantContext (your app).
// See references/finbuckle-tenant-info.md for the full entity and TenantDbContext.
```

### Step 5 — Tenant Enrichment Middleware

Resolve the Finbuckle tenant and hydrate the `AsyncLocal` accessor so AgentBlazor's runtime can see it:

```csharp
app.UseMultiTenant();

app.Use(async (context, next) =>
{
    var tenantInfo = context.GetMultiTenantContext<TenantInfo>()?.TenantInfo;
    if (tenantInfo is not null)
    {
        var accessor = context.RequestServices.GetRequiredService<TenantContextAccessor>();
        accessor.TenantContext = tenantInfo;
    }
    await next();
});
```

### Step 6 — Register AgentBlazor

```csharp
// The proxy IChatClient IS the IChatClient — register it as singleton.
// ChatClientRuntimeAdapter picks it up via normal DI.
builder.Services.AddSingleton<IChatClient, TenantAwareChatClient>();

builder.Services.AddAgentBlazor(options =>
{
    // Do NOT call UseOpenAI / UseAzureOpenAI — the proxy handles it.
    // Do NOT call UseRuntimeAdapter — the default ChatClientRuntimeAdapter is correct.

    options.UseMiddleware<TenantCostControlMiddleware>();
    options.ConfigureBuilder(ab =>
    {
            // Entity columns (TenantId, BaseSessionId, AgentName) — see ab-entity-design
            ab.UseConversationStore(sp => new TenantConversationStore(
            sp.GetRequiredService<TenantContextAccessor>(),
            sp.GetRequiredService<IOptions<ConversationOptions>>(),
            sp.GetRequiredService<IDbContextFactory<ConversationDbContext>>()));
        ab.AddAgent("Hub Agent", agent => { /* ... */ });
    });
});
```

### Step 7 — Blazor Circuit Tenant Context

Blazor Interactive Server resolves the tenant on the initial HTTP request. The SignalR circuit survives reconnections, so write a cookie on first render so Finbuckle's `CookieResolutionStrategy` can re-resolve the tenant after a disconnect:

```razor
@* App.razor or TenantLayout.razor *@
@code {
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private TenantContextAccessor TenantAccessor { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        var tenantId = TenantAccessor.TenantContext?.TenantId;
        if (tenantId is not null)
        {
            await JS.InvokeVoidAsync("eval",
                $"document.cookie = 'TenantId={tenantId}; path=/; SameSite=Lax'");
        }
    }
}
```

The `AgentChatSurface.SessionId` parameter may optionally include the tenant prefix for conversation store isolation (defense-in-depth):

<!-- Tenant prefix is optional defense-in-depth — the TenantId column already provides tenant isolation.
     Without the prefix, all queries still filter by TenantId from TenantContextAccessor. -->
```razor
<AgentChatSurface SessionId="@($"{Tenant.TenantId}:{ComponentRegistry.SessionId}")" />
```

This also ensures `AgentChatBar` session lists are naturally tenant-scoped — the embed ded tenant ID in session keys means `GetActiveSessionsAsync` / `GetSessionsForUserAsync` return only the current tenant’s sessions.

## Reference Files

- **[Finbuckle Tenant Info](references/finbuckle-tenant-info.md)** — Complete `TenantInfo` entity with LLM config, budgets, and connection strings
- **[Tenant-Aware Chat Client](references/tenant-aware-chat-client.md)** — Full `TenantAwareChatClient` implementation with streaming, caching, and provider fallback
- **[Per-Tenant Conversation Store](references/tenant-conversation-store.md)** — EF Core `IConversationStore` with `TenantId` isolation + column-level filtering
- **[Per-Tenant EF Core](references/ef-core-per-tenant.md)** — Finbuckle `MultiTenantDbContext`, migration strategy, `TenantDbContextFactory`
- **[Middleware Stack](references/middleware-stack.md)** — `TenantCostControlMiddleware`, audit logging, per-tenant request logging via `IAgentTurnMiddleware`
- **[BFF API Client](references/bff-api-client.md)** — Typed `HttpClient` wrapper with tenant header injection and OAuth On-Behalf-Of
- **[Tenant Provisioning](references/tenant-provisioning.md)** — New tenant database creation, migration automation, seed data
- **[Fresh-Scope Context Bridging](references/fresh-scope-context-bridging.md)** — seeding fresh AsyncLocal circuit/request context into singleton stores (remediation for the ❌ Scoped ITenantContext row)

## Error States & Edge Cases

| Scenario | Behavior |
|---|---|
| Tenant not resolved (landing page, unauthenticated) | `TenantContextAccessor.TenantContext` is null. Middleware + proxy skip silently. `AgentChatSurface` shows “no provider” error. |
| Tenant’s LLM provider unreachable | `TenantAwareChatClient.CompleteAsync` throws. `ChatClientRuntimeAdapter` catches and returns an error response to the user. |
| API key invalid or expired | Same as above — provider throws, adapter catches, user sees error message. |
| Background job triggers agent (no HttpContext) | `TenantContextAccessor.TenantContext` is null because no ASP.NET middleware set it. The caller must explicitly set it before invoking the agent. |
| SignalR circuit reconnect | Cookie written on first render (Step 7) ensures Finbuckle re-resolves the tenant on reconnect. |
| Conversation store `TenantId` filtering misses a query | Session keys may embed `{tenantId}:` prefix (optional defense-in-depth convention) — even if a query forgets the `TenantId` filter, other tenants' sessions won't match. |

## Design Limitations

- **`ConversationOptions` is a singleton snapshot.** `MaxTurnsPerSession`, `SessionTimeout`, etc. are resolved once at startup. Per-tenant overrides set via `TenantInfo` fields (e.g., `tenant.MaxTurnsPerSession`) must be read directly from `TenantContextAccessor` inside the store, not from `IOptions<ConversationOptions>`. The `TenantConversationStore` in the reference file demonstrates this pattern.
- **Conversation history lives in a shared database.** The `ConversationDbContext` uses a single connection string. Tenant isolation depends on the `TenantId` column filter. For per-tenant conversation databases, use Finbuckle’s `WithPerTenantConnectionString` and pass the resolved connection string to `IDbContextFactory<T>` at resolution time.
- **`TenantInfo` explicit interface members.** `TenantId` and `TenantName` are implemented explicitly (`string ITenantContext.TenantId`). Access via `((ITenantContext)tenantInfo).TenantId` or through the `TenantContextAccessor` which returns `ITenantContext`.

### ❌ Factory-per-adapter with `UseRuntimeAdapter`

```csharp
// BROKEN — adapters are singletons, IChatClient is baked in forever,
// tenant context can't be resolved from root provider.
options.UseRuntimeAdapter(sp =>
{
    var tenant = sp.GetRequiredService<ITenantContext>(); // throws at startup
    var client = factory.CreateClient(tenant);
    return new ChatClientRuntimeAdapter(client, /* ...19 more params... */);
});
```

### ❌ Scoped ITenantContext in singleton store

```csharp
// BROKEN — singleton IConversationStore can't consume scoped ITenantContext.
// Use AsyncLocal TenantContextAccessor (singleton) instead.
```

> **Concrete instance — the fresh-scope rewrite path:** AgentBlazor's `SingletonConversationStoreProxy` (BFF wiring) hits exactly this limitation when the conversation rewrite path runs outside the pushed execution scope: the fresh DI scope has no tenant context and a null scoped `ILifelineSessionContext`, so context must be bridged from a per-identity static cache seeded from the AsyncLocal accessor. AsyncLocal flows across awaits but NOT across `IServiceScopeFactory.CreateScope()`. Remediation: see [Fresh-scope context bridging](references/fresh-scope-context-bridging.md).

### ❌ Baking tenant into session ID without async context flow

The tenant must survive the whole turn pipeline. Relying only on `SessionId` embedding works for conversation store isolation, but doesn't help the LLM provider, cost control, or audit logging — those need `AsyncLocal`.

## Key Integration Points

| Concern | Mechanism |
|---|---|
| **Tenant identity flow** | `TenantContextAccessor` (AsyncLocal), set by Finbuckle middleware |
| **Per-tenant LLM** | `TenantAwareChatClient` (proxy `IChatClient`, singleton) |
| **Agent middleware** | `IAgentTurnMiddleware` reads `TenantContextAccessor` to enforce budgets, log per-tenant |
| **Conversation store** | Custom `IConversationStore` with `TenantId` filtering OR `TenantId` embedded in session key |
| **Application data** | Finbuckle `MultiTenantDbContext` + `WithPerTenantConnectionString` |
| **BFF → API** | Typed `HttpClient` with `X-Tenant-Id` header + OAuth OBO |
