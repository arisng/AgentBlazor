# Multi-Tenant Production Blueprint: AgentBlazor in a Blazor Interactive SSR + BFF Platform

> **Based on** `AgentBlazor.Demo` (v0.2.22) architecture analysis  
> **Target** Blazor Interactive Server (InteractiveServer) with Backend-for-Frontend (BFF) pattern  
> **Date** 2026-07-13

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Project Structure](#2-project-structure)
3. [Tenant Resolution & Context](#3-tenant-resolution--context)
4. [Per-Tenant LLM Provider Configuration](#4-per-tenant-llm-provider-configuration)
5. [Per-Tenant Cost Control](#5-per-tenant-cost-control)
6. [Per-Tenant SQL Server Database](#6-per-tenant-sql-server-database)
7. [BFF Pattern Integration](#7-bff-pattern-integration)
8. [AgentBlazor Configuration Pipeline](#8-agentblazor-configuration-pipeline)
9. [Middleware Stack](#9-middleware-stack)
10. [Routing & Layout Architecture](#10-routing--layout-architecture)
11. [Workflow Capability Patterns](#11-workflow-capability-patterns)
12. [Observability & Monitoring](#12-observability--monitoring)
13. [Deployment Pipeline](#13-deployment-pipeline)
14. [Production Readiness Checklist](#14-production-readiness-checklist)
15. [Appendix: Key Code Patterns](#15-appendix-key-code-patterns)

---

## 1. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                          USER BROWSER                                 │
│     Blazor Interactive Server (SignalR ─ circuit per session)        │
└──────────────────────────┬───────────────────────────────────────────┘
                           │ SignalR
                           ▼
┌──────────────────────────────────────────────────────────────────────┐
│                     BFF HOST (ASP.NET Core 10)                       │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │                  AgentBlazor Runtime                         │    │
│  │  ┌──────────┐  ┌─────────────┐  ┌────────────────────────┐  │    │
│  │  │ Chat     │  │ Middleware  │  │ Agent/Workflow         │  │    │
│  │  │ Client   │  │ Pipeline   │  │ Registry               │  │    │
│  │  │ Adapter  │  │ (logging,   │  │ ([AgentCapability]     │  │    │
│  │  │          │  │  cost,      │  │  + [AgentAction])      │  │    │
│  │  │          │  │  tenant)    │  │                        │  │    │
│  │  └────┬─────┘  └─────────────┘  └────────────────────────┘  │    │
│  └──────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │              Tenant Resolution Layer                         │    │
│  │  ┌─────────────────┐  ┌────────────────┐  ┌───────────────┐  │    │
│  │  │ ITenantContext   │  │ TenantScoped   │  │ Tenant Db    │  │    │
│  │  │ (per-circuit)    │  │ ProviderCache  │  │ Factory      │  │    │
│  │  └─────────────────┘  └────────────────┘  └───────┬───────┘  │    │
│  └──────────────────────────────────────────────────────┬────────┘    │
│                                                         │             │
│  ┌──────────────────────────────────────────────────────┐│             │
│  │  BFF HTTP Client Layer                               ││             │
│  │  HttpClient (named per tenant) → API App              ││             │
│  └──────────────────────────────────────────────────────┘│             │
└──────────────────────────┬────────────────────────────────────────────┘
                           │ HTTP (REST)
                           ▼
┌──────────────────────────────────────────────────────────────────────┐
│                       API APP (ASP.NET Core 10)                      │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │  Per-Tenant Data Access                                       │    │
│  │  ┌─────────────┐  ┌───────────────────┐  ┌────────────────┐  │    │
│  │  │ Tenant      │  │ SQL Server        │  │ Business       │  │    │
│  │  │ Identifier  │  │ (db per tenant)   │  │ Logic          │  │    │
│  │  └─────────────┘  └───────────────────┘  └────────────────┘  │    │
│  └──────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │  Shared Services (Auth, Billing, Audit)                      │    │
│  └──────────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

| Decision | Rationale |
|---|---|
| **Blazor Interactive Server** | Circuit-per-session enables in-memory scoped state, real-time agent UI updates, and `IAgentComponentRegistry` per circuit |
| **BFF pattern** | The Blazor app calls an API app rather than accessing databases directly. This keeps the Blazor host stateless for scale-out and places data access behind a secured API boundary |
| **AgentBlazor in the BFF** | Agent runtime (LLM calls, capability execution, middleware) lives in the BFF. The API app is a pure data/domain layer called by workflows via `HttpClient` |
| **Per-tenant SQL Server** | Each tenant gets its own database (connection string stored in tenant config). The API app resolves the correct database via `ITenantContext` |
| **Per-tenant LLM provider** | Each tenant can configure their own provider (OpenAI, Azure OpenAI, Ollama) or use a platform-default. The BFF resolves the provider per request via `ITenantContext` |
| **Per-tenant cost control** | Daily/monthly budgets enforced per tenant in the AgentBlazor middleware pipeline, with configurable token pricing and hard caps |

---

## 2. Project Structure

```
src/
├── YourApp.Bff/                          # BFF Host (Blazor Interactive Server)
│   ├── Program.cs                        # Entry point
│   ├── YourApp.Bff.csproj
│   ├── appsettings.json                  # Base config
│   ├── appsettings.{TenantId}.json       # Per-tenant overrides (dev)
│   ├── Components/
│   │   ├── App.razor
│   │   ├── Routes.razor
│   │   ├── _Imports.razor
│   │   ├── Layout/
│   │   │   ├── TenantLayout.razor        # Primary layout with AgentChatSurface/Widget
│   │   │   ├── LandingLayout.razor       # Unauthenticated marketing area
│   │   │   └── ReconnectModal.razor
│   │   ├── Pages/
│   │   │   ├── Home.razor
│   │   │   ├── Error.razor
│   │   │   ├── NotFound.razor
│   │   │   └── Workflows/
│   │   │       └── ...                   # Per-workflow pages
│   │   └── Shared/
│   │       ├── WorkflowDecisionSupport.razor
│   │       └── ...
│   ├── Configuration/
│   │   ├── TenantOptions.cs              # All tenant-level settings
│   │   ├── TenantProviderOptions.cs      # Per-tenant LLM provider config
│   │   ├── TenantCostControlOptions.cs   # Per-tenant budget & pricing
│   │   ├── TenantDatabaseOptions.cs      # Per-tenant connection strings
│   │   └── GlobalSecurityOptions.cs      # System-wide security settings
│   ├── Services/
│   │   ├── TenantContext.cs              # ITenantContext implementation
│   │   ├── TenantResolutionMiddleware.cs # Extracts tenant from host/header/cookie
│   │   ├── TenantProviderFactory.cs      # Creates IChatClient per tenant
│   │   ├── TenantCostTracker.cs          # Per-tenant cost accounting
│   │   ├── TenantDatabaseFactory.cs      # BFF-side DB factory if needed
│   │   ├── Workflows/
│   │   │   ├── ...Capabilities.cs        # [AgentCapability] classes
│   │   │   └── ...Service.cs             # Workflow state management
│   │   ├── Logging/
│   │   │   ├── TenantChatRequestLog.cs   # Per-tenant JSONL logger
│   │   │   ├── TenantTrafficLog.cs
│   │   │   └── TenantLogMiddleware.cs    # IAgentTurnMiddleware impl
│   │   └── BffApi/
│   │       ├── IBffApiClient.cs          # Interface for API calls
│   │       ├── BffApiClient.cs           # HttpClient wrapper
│   │       └── BffApiOptions.cs
│   └── wwwroot/
│       ├── app.css                       # Design tokens
│       └── ... (static assets)
│
├── YourApp.Api/                          # API App (REST backend)
│   ├── Program.cs
│   ├── Controllers/
│   │   └── ...                           # Per-domain controllers
│   ├── Data/
│   │   ├── TenantDbContextFactory.cs     # Resolves DbContext per tenant
│   │   ├── Migrations/
│   │   └── ... (EF Core entities)
│   ├── Services/
│   │   └── ...
│   └── TenantConfigurationStore/        # Where tenant configs live
│       ├── ITenantConfigurationStore.cs  # Fetches tenant metadata
│       └── TenantConfigurationStore.cs
│
├── YourApp.Shared/                       # Shared DTOs, interfaces
│   ├── Dtos/
│   ├── ITenantContext.cs                 # Shared interface
│   └── AgentBlazorExtensions/           # Your app-level AgentBlazor helpers
│       ├── TenantAwareAgentBuilder.cs
│       ├── TenantAwareChatClientFactory.cs
│       └── TenantCostControlMiddleware.cs
│
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
└── Dockerfile
```

---

## 3. Tenant Resolution & Context

### 3.1 `ITenantContext` Interface (shared between BFF and API)

```csharp
public interface ITenantContext
{
    string TenantId { get; }
    string? TenantSlug { get; }
    TenantPlan Plan { get; }               // Free, Pro, Enterprise
    TenantProviderConfig Provider { get; }  // LLM provider config for this tenant
    TenantCostConfig CostControl { get; }   // Budget limits for this tenant
    TenantDatabaseConfig Database { get; }  // Connection string for this tenant
    IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed record TenantProviderConfig
{
    public string ProviderType { get; init; } = "OpenAI";  // OpenAI, AzureOpenAI, Ollama
    public string? ApiKey { get; init; }
    public string? Endpoint { get; init; }
    public string Model { get; init; } = "gpt-4o-mini";
    public string? AzureDeploymentName { get; init; }
    public int MaxTokens { get; init; } = 4096;
    public double Temperature { get; init; } = 0.7;
}

public sealed record TenantCostConfig
{
    public bool Enabled { get; init; } = true;
    public decimal DailyBudgetUsd { get; init; } = 5.00m;
    public decimal MonthlyBudgetUsd { get; init; } = 100.00m;
    public decimal InputTokenCostPerMillion { get; init; } = 0.15m;
    public decimal OutputTokenCostPerMillion { get; init; } = 0.60m;
    public bool HardCapEnabled { get; init; } = true;
    public string NotificationEmail { get; init; } = "";
}

public sealed record TenantDatabaseConfig
{
    public string ConnectionString { get; init; } = "";
    public string Provider { get; init; } = "SqlServer";  // SqlServer, Postgres, etc.
    public bool UseDatabasePerTenant { get; init; } = true;
}
```

### 3.2 Tenant Resolution Strategy

The BFF must resolve the tenant identity on every request. The Demo app uses IP-based rate limiting — production multi-tenancy needs richer resolution:

```
Resolution Priority (first match wins):
1. Host header subdomain:  {tenant}.yourapp.com
2. X-Tenant-Id header:      (set by API gateway / reverse proxy)
3. Cookie (authenticated):  Set after login, most reliable for SignalR
4. URL path prefix:         /{tenant}/workflows/...
5. Default tenant:          For unauthenticated landing pages
```

```csharp
// TenantResolutionMiddleware.cs
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor accessor)
    {
        var tenantId = ResolveTenantId(context);
        if (tenantId is not null)
        {
            var tenantConfig = await FetchTenantConfigurationAsync(tenantId, context.RequestAborted);
            if (tenantConfig is not null)
            {
                accessor.TenantContext = new TenantContext(tenantId, tenantConfig);
            }
        }
        await _next(context);
    }

    private static string? ResolveTenantId(HttpContext context)
    {
        // 1. Subdomain
        var host = context.Request.Host.Host;
        if (host.Contains('.') && !host.StartsWith("www."))
        {
            var parts = host.Split('.');
            return parts[0]; // {tenant}.yourapp.com
        }

        // 2. Header
        var header = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header)) return header;

        // 3. Cookie (for SignalR circuits)
        var cookie = context.Request.Cookies["TenantId"];
        if (!string.IsNullOrWhiteSpace(cookie)) return cookie;

        // 4. Path prefix
        var path = context.Request.Path.Value;
        if (path?.StartsWith("/") == true && path.Length > 1)
        {
            var firstSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
            if (IsValidTenantSlug(firstSegment)) return firstSegment;
        }

        return null; // unauthenticated / landing
    }
}
```

### 3.3 Blazor Circuit-Scoped Tenant Context

Blazor Interactive Server circuits maintain state across SignalR reconnections. The `ITenantContext` must be **scoped (per-circuit)**:

```csharp
// In Program.cs
builder.Services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
builder.Services.AddScoped<ITenantContext>(sp =>
{
    var accessor = sp.GetRequiredService<ITenantContextAccessor>();
    return accessor.TenantContext ?? throw new InvalidOperationException("No tenant context");
});

// WARNING: IChatClient is Singleton by default from Microsoft.Extensions.AI
// You must create per-tenant IChatClient instances dynamically because
// tenant context is Scoped per circuit.
builder.Services.AddScoped<ITenantChatClientFactory, TenantChatClientFactory>();
```

### 3.4 Tenant Configuration Store

Store tenant configurations in a secure, fast lookup (database, Redis, or encrypted JSON):

```json
// Example tenant config document (in DB or config store)
{
  "tenantId": "acme-corp",
  "plan": "Enterprise",
  "provider": {
    "providerType": "AzureOpenAI",
    "endpoint": "https://acme-openai.openai.azure.com",
    "deploymentName": "gpt-4o",
    "apiKey": "{{ENCRYPTED}}",
    "model": "gpt-4o",
    "maxTokens": 8192
  },
  "costControl": {
    "enabled": true,
    "dailyBudgetUsd": 50.00,
    "monthlyBudgetUsd": 1000.00,
    "inputTokenCostPerMillion": 2.50,
    "outputTokenCostPerMillion": 10.00,
    "hardCapEnabled": true,
    "notificationEmail": "admin@acme.com"
  },
  "database": {
    "connectionString": "Server=sql-cluster.database.windows.net;Database=AgentBlazor_acme;...",
    "provider": "SqlServer"
  }
}
```

---

## 4. Per-Tenant LLM Provider Configuration

### 4.1 The Challenge

AgentBlazor's `AddAgentBlazor()` registers an `IChatClient` as a **singleton** in DI. In a multi-tenant setup, each tenant needs its own provider config (different API keys, endpoints, models, deployment names). The solution is a **tenant-aware factory** that creates `IChatClient` instances per request based on the resolved tenant.

### 4.2 Tenant-Aware Chat Client Factory

```csharp
public interface ITenantChatClientFactory
{
    IChatClient CreateClient(ITenantContext tenant);
}

public sealed class TenantChatClientFactory : ITenantChatClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TenantChatClientFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public IChatClient CreateClient(ITenantContext tenant)
    {
        var provider = tenant.Provider;

        return provider.ProviderType switch
        {
            "OpenAI" => CreateOpenAIClient(provider),
            "AzureOpenAI" => CreateAzureOpenAIClient(provider),
            "Ollama" => CreateOllamaClient(provider),
            _ => throw new NotSupportedException($"Provider '{provider.ProviderType}' not supported")
        };
    }

    private IChatClient CreateOpenAIClient(TenantProviderConfig config)
    {
        var client = new OpenAIClient(config.ApiKey ?? throw new InvalidOperationException("API key required"));
        return client.GetChatClient(config.Model).AsIChatClient();
    }

    private IChatClient CreateAzureOpenAIClient(TenantProviderConfig config)
    {
        var endpoint = new Uri(config.Endpoint ?? throw new InvalidOperationException("Endpoint required"));
        var client = new AzureOpenAIClizerConnection(endpoint, new ApiKeyCredential(config.ApiKey ?? ""));
        return client.GetChatClient(config.AzureDeploymentName ?? config.Model).AsIChatClient();
    }

    private IChatClient CreateOllamaClient(TenantProviderConfig config)
    {
        var endpoint = config.Endpoint ?? "http://127.0.0.1:11434/v1";
        var client = new OpenAIClient(config.ApiKey ?? "ollama", new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        return client.GetChatClient(config.Model).AsIChatClient();
    }
}
```

### 4.3 Registering with AgentBlazor

AgentBlazor's `AgentBlazorRegistrationOptions.UseOpenAI()` / `UseOllama()` register a singleton `IChatClient`. For multi-tenancy, you need to bypass these and supply the runtime adapter directly, or use the `UseRuntimeAdapter<T>()` hook.

> **Note (v0.2.23+):** `AgentBlazorRegistrationOptions.ConfigureChatOptions()` applies only to the singleton `IChatClient` registered by `UseOpenAI()` / `UseAzureOpenAI()` / `UseOllama()`. When you replace the client with a per-tenant proxy (as below), the hook is bypassed — pin `ChatOptions` per-tenant inside your factory instead. For example, GPT-5.6-family models reject function tools with HTTP 400 (`reasoning_effort`) unless effort is pinned: `o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }` (requires `using Microsoft.Extensions.AI;`).

```csharp
// In Program.cs — per-tenant provider registration
builder.Services.AddScoped<ITenantChatClientFactory, TenantChatClientFactory>();
builder.Services.AddHttpClient(); // For factory use

builder.Services.AddAgentBlazor(options =>
{
    // Do NOT call options.UseOpenAI() or UseOllama() here.
    // Instead, use a custom runtime adapter that resolves the provider per-request.

    options.UseRuntimeAdapter(sp =>
    {
        var tenantFactory = sp.GetRequiredService<ITenantChatClientFactory>();
        var tenantContext = sp.GetRequiredService<ITenantContext>();
        var chatClient = tenantFactory.CreateClient(tenantContext);
        return ActivatorUtilities.CreateInstance<TenantAwareRuntimeAdapter>(sp, chatClient);
    });

    // Or use the stock ChatClientRuntimeAdapter but with a scoped IChatClient:
    // This requires registering a Scoped IChatClient that delegates to the factory.
});
```

Alternatively, register a **scoped `IChatClient`** that wraps the factory:

```csharp
// Scoped IChatClient registration
builder.Services.AddScoped<IChatClient>(sp =>
{
    var factory = sp.GetRequiredService<ITenantChatClientFactory>();
    var tenant = sp.GetRequiredService<ITenantContext>();
    return factory.CreateClient(tenant);
});

builder.Services.AddAgentBlazor(options =>
{
    // AgentBlazor will pick up the Scoped IChatClient via DI
    // (This may need a thin adapter since AgentBlazor expects singleton IChatClient)
    options.UseRuntimeAdapter<TenantAwareRuntimeAdapter>();
});
```

### 4.4 Provider Options Matrix

| Provider | Config Required | Notes |
|---|---|---|
| **OpenAI** | `ApiKey`, `Model` | Standard OpenAI chat models |
| **Azure OpenAI** | `Endpoint`, `DeploymentName`, `ApiKey` (or `TokenCredential`) | Production recommendation — supports managed identity |
| **Ollama (local)** | `Endpoint`, `Model` | Self-hosted, no API key. Good for dev/test |
| **OriginAI** | `Endpoint`, `ApiKey`, `ChatSource` | Custom provider. Uses `IHttpClientFactory` with `X-Tenant-Info` header |
| **Custom** | `Endpoint`, `ApiKey`, `ModelProvider` | Bring-your-own provider via `IChatClient` |

### 4.5 Provider Caching Strategy

Creating `IChatClient` instances per request is expensive (network connections). Cache per tenant:

```csharp
public sealed class CachedTenantChatClientFactory : ITenantChatClientFactory
{
    private readonly ITenantChatClientFactory _inner;
    private readonly ConcurrentDictionary<string, IChatClient> _cache = new();

    public CachedTenantChatClientFactory(ITenantChatClientFactory inner) => _inner = inner;

    public IChatClient CreateClient(ITenantContext tenant)
    {
        return _cache.GetOrAdd(tenant.TenantId, _ => _inner.CreateClient(tenant));
    }
}
```

For higher scale, use a `MemoryCache` with sliding expiration and eviction on tenant config changes.

---

## 5. Per-Tenant Cost Control

### 5.1 Cost Tracking Architecture

Based on the Demo app's `JsonlDemoChatRequestLog` + daily budget enforcement pattern, extended per-tenant:

```
                Agent Turn Request
                       │
                       ▼
┌─────────────────────────────────────┐
│  TenantCostControlMiddleware         │
│  (IAgentTurnMiddleware)             │
│                                     │
│  1. Resolve tenant from context     │
│  2. Read today's cost from store    │
│  3. If over daily budget: BLOCK     │
│  4. If over monthly budget: BLOCK   │
│  5. Allow → log usage on response   │
└─────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────┐
│  LLM Provider Call                  │
│  (returns token counts)             │
└─────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────┐
│  Cost Accounting                    │
│  1. prompt_tokens × input_cost      │
│  2. output_tokens × output_cost     │
│  3. Total = accumulated to store    │
│  4. Append to per-tenant JSONL      │
└─────────────────────────────────────┘
```

### 5.2 `TenantCostControlMiddleware` (IAgentTurnMiddleware)

```csharp
[UsedImplicitly]
public sealed class TenantCostControlMiddleware : IAgentTurnMiddleware
{
    private readonly TenantCostControlOptions _options;
    private readonly ITenantCostStore _costStore;
    private readonly ILogger<TenantCostControlMiddleware> _logger;

    public TenantCostControlMiddleware(
        IOptions<TenantCostControlOptions> options,
        ITenantCostStore costStore,
        ILogger<TenantCostControlMiddleware> logger)
    {
        _options = options.Value;
        _costStore = costStore;
        _logger = logger;
    }

    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            await next(ct);
            return;
        }

        var tenantId = context.Items.TryGetValue("TenantId", out var tid) ? tid?.ToString() : null;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await next(ct);
            return;
        }

        // Get tenant-specific budget
        var budget = await _costStore.GetBudgetAsync(tenantId, ct);
        var todayUtc = DateTime.UtcNow.Date;

        // Check daily budget
        var dailySpend = await _costStore.GetDailySpendAsync(tenantId, todayUtc, ct);
        if (dailySpend >= budget.DailyBudgetUsd)
        {
            context.Response = new AgentTurnResponse
            {
                Text = $"Daily cost limit of ${budget.DailyBudgetUsd:F2} reached for your tenant. "
                     + "Please contact your administrator or wait until tomorrow.",
                Status = AgentTurnStatus.Blocked
            };
            return;
        }

        // Check monthly budget
        var monthlySpend = await _costStore.GetMonthlySpendAsync(tenantId, todayUtc, ct);
        if (budget.HardCapEnabled && monthlySpend >= budget.MonthlyBudgetUsd)
        {
            context.Response = new AgentTurnResponse
            {
                Text = $"Monthly cost limit of ${budget.MonthlyBudgetUsd:F2} reached for your tenant. "
                     + "Please contact your administrator to increase the budget.",
                Status = AgentTurnStatus.Blocked
            };
            return;
        }

        // Wrap the next delegate to capture token usage
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(ct);

            // After execution, record costs from context.Items
            if (context.Items.TryGetValue("TokenUsage", out var usage) && usage is TokenUsage tokenUsage)
            {
                var cost = CalculateCost(tokenUsage, budget);
                await _costStore.RecordUsageAsync(tenantId, new CostRecord
                {
                    TimestampUtc = DateTime.UtcNow,
                    PromptTokens = tokenUsage.PromptTokens,
                    CompletionTokens = tokenUsage.CompletionTokens,
                    TotalTokens = tokenUsage.TotalTokens,
                    EstimatedCostUsd = cost,
                    RequestId = context.Request.RequestId
                }, ct);

                if (dailySpend + cost >= budget.DailyBudgetUsd * 0.8m)
                {
                    _logger.LogWarning(
                        "Tenant {TenantId} has used {Pct}% of daily budget ({Spend}/{Budget})",
                        tenantId, (dailySpend + cost) / budget.DailyBudgetUsd * 100,
                        dailySpend + cost, budget.DailyBudgetUsd);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cost tracking failed for tenant {TenantId}", tenantId);
            throw; // Don't block the user for a cost-tracking failure
        }
    }

    private decimal CalculateCost(TokenUsage usage, TenantBudget budget)
    {
        var inputCost = usage.PromptTokens * budget.InputTokenCostPerMillion / 1_000_000m;
        var outputCost = usage.CompletionTokens * budget.OutputTokenCostPerMillion / 1_000_000m;
        return inputCost + outputCost;
    }
}
```

### 5.3 Cost Store Options

| Store | Pros | Cons |
|---|---|---|
| **JSONL file** (per tenant) | Simple, no infra | Not scalable across multiple BFF instances |
| **SQL Server** | Durable, queryable, shared across instances | Extra DB load |
| **Redis** | Fast, TTL-based auto-expiry, counters | Volatile if not persisted |
| **Azure Cosmos DB** | Globally distributed, per-tenant RU isolation | Higher latency than Redis |

**Recommendation**: Redis for real-time budget counters + SQL Server for audit trail. Use `StackExchange.Redis` with `StringIncrement` + `KeyExpire` for daily/monthly counters.

```csharp
public sealed class RedisCostStore : ITenantCostStore
{
    private readonly IDatabase _redis;
    private readonly ICostAuditLog _auditLog;

    public RedisCostStore(IConnectionMultiplexer redis, ICostAuditLog auditLog)
    {
        _redis = redis.GetDatabase();
        _auditLog = auditLog;
    }

    public async Task RecordUsageAsync(string tenantId, CostRecord record, CancellationToken ct)
    {
        var dateKey = $"cost:daily:{tenantId}:{record.TimestampUtc:yyyy-MM-dd}";
        var monthKey = $"cost:monthly:{tenantId}:{record.TimestampUtc:yyyy-MM}";

        var tx = _redis.CreateTransaction();
        _ = tx.StringIncrementAsync(dateKey, (double)record.EstimatedCostUsd);
        _ = tx.KeyExpireAsync(dateKey, TimeSpan.FromDays(90)); // auto-cleanup
        _ = tx.StringIncrementAsync(monthKey, (double)record.EstimatedCostUsd);
        _ = tx.KeyExpireAsync(monthKey, TimeSpan.FromDays(180));
        await tx.ExecuteAsync();

        await _auditLog.AppendAsync(tenantId, record, ct);
    }

    public async Task<decimal> GetDailySpendAsync(string tenantId, DateTime date, CancellationToken ct)
    {
        var val = await _redis.StringGetAsync($"cost:daily:{tenantId}:{date:yyyy-MM-dd}");
        return (decimal)(val.HasValue ? (double)val : 0);
    }
}
```

### 5.4 Tier-Based Budget Presets

| Plan | Daily Budget | Monthly Budget | Provider | Model |
|---|---|---|---|---|
| **Free** | $0.50 | $10.00 | OpenAI only | `gpt-4o-mini` |
| **Pro** | $5.00 | $100.00 | OpenAI / Azure | `gpt-4o-mini` or custom |
| **Enterprise** | Custom | Custom | Any | Any |

---

## 6. Per-Tenant SQL Server Database

### 6.1 Architecture

```
┌─────────────────────────────────────────────┐
│              API App                          │
│                                               │
│  ┌───────────────────────────────────────┐    │
│  │ TenantDbContextFactory                │    │
│  │                                       │    │
│  │  ITenantContext.TenantId ──────────►  │    │
│  │                                       │    │
│  │  Lookup connection string in:         │    │
│  │    a) TenantConfigurationStore (DB)   │    │
│  │    b) Encrypted config file           │    │
│  │    c) Hash-based routing (shard)      │    │
│  │                                       │    │
│  │  Create DbContext with connection     │    │
│  └───────────────────────────────────────┘    │
│                                               │
│  ┌──────────────────┐  ┌──────────────────┐   │
│  │ DbContext        │  │ DbContext        │   │
│  │ (Tenant: Acme)   │  │ (Tenant: Beta)   │   │
│  │ Server=sql1;     │  │ Server=sql1;     │   │
│  │ Database=AcmeDB  │  │ Database=BetaDB  │   │
│  └──────────────────┘  └──────────────────┘   │
└─────────────────────────────────────────────┘
```

### 6.2 Database Resolution Strategies

| Strategy | How It Works | When to Use |
|---|---|---|
| **Database-per-tenant** | Separate DB per tenant on shared SQL Server | Most common. Good isolation, easy backup/restore per tenant |
| **Schema-per-tenant** | Same DB, different schemas (`Acme.*`, `Beta.*`) | Lower connection overhead, but weaker isolation |
| **Server-per-tenant** | Different SQL Servers per tenant | Enterprise with geographic/data-sovereignty requirements |
| **Shard-per-tenant** | Hash-based routing to one of N shards | Very large scale (10K+ tenants) |

### 6.3 `TenantDbContextFactory` (in API App)

```csharp
public sealed class TenantDbContextFactory
{
    private readonly ITenantConfigurationStore _configStore;
    private readonly ITenantContextAccessor _contextAccessor;

    public TenantDbContextFactory(
        ITenantConfigurationStore configStore,
        ITenantContextAccessor contextAccessor)
    {
        _configStore = configStore;
        _contextAccessor = contextAccessor;
    }

    public async Task<TDbContext> CreateDbContextAsync<TDbContext>(
        CancellationToken ct = default)
        where TDbContext : DbContext
    {
        var tenant = _contextAccessor.TenantContext
            ?? throw new InvalidOperationException("No tenant context available");

        // Fetch or cache connection string
        var config = await _configStore.GetDatabaseConfigAsync(tenant.TenantId, ct);
        var connectionString = config.ConnectionString;

        // Build DbContext with the tenant-specific connection
        var optionsBuilder = new DbContextOptionsBuilder<TDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(3);
            sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            // Tenant-specific EF Core configuration
        });

        var dbContext = (TDbContext)Activator.CreateInstance(typeof(TDbContext), optionsBuilder.Options)!;
        return dbContext;
    }
}
```

### 6.4 Migration Strategy

Database-per-tenant means running migrations per tenant:

```csharp
// During tenant provisioning
public async Task ProvisionTenantDatabaseAsync(string tenantId, CancellationToken ct)
{
    var factory = sp.GetRequiredService<TenantDbContextFactory>();
    await using var db = await factory.CreateDbContextAsync<YourDbContext>(ct);
    await db.Database.MigrateAsync(ct);

    // Or for existing databases, check and apply pending migrations
    var pending = await db.Database.GetPendingMigrationsAsync(ct);
    if (pending.Any())
    {
        _logger.LogInformation("Applying {Count} migrations to tenant {Tenant}", pending.Count(), tenantId);
        await db.Database.MigrateAsync(ct);
    }
}
```

For rolling migrations across hundreds of tenants, use a **migration job queue** (Azure Queue / SQS) with:
- Batch processing (e.g., 10 tenants at a time)
- Per-tenant migration status tracking
- Rollback/retry for failed migrations

### 6.5 BFF-Side Database Access

The BFF should ideally **not** access the database directly — it calls the API via `HttpClient`. However, AgentBlazor's Pro features (action history, audit log, analytics) use SQLite files by default (`SqliteActionHistoryStore`, `SqliteUsageAnalyticsService`). These are **per-BFF-instance local files**, not suitable for scale-out.

For production multi-tenant use with the BFF pattern:

```csharp
// Option A: Route Pro store operations through the API
builder.Services.AddAgentBlazor(options =>
{
    options.UseProLicense(licenseKey, dataDirectory);
    // The license enables SQLite-backed stores in dataDirectory.
    // For scale-out, you'll need custom implementations that call the API.
});

// Option B: Use shared SQL Server for Pro stores (requires customization)
// Replace the built-in stores with custom implementations that use SQL Server
// via the tenant's connection string (through the API).
```

---

## 7. BFF Pattern Integration

### 7.1 Why BFF for AgentBlazor?

| Concern | Without BFF | With BFF |
|---|---|---|
| **Database access** | BFF connects directly to SQL Server | API owns all data access |
| **Auth tokens** | Blazor manages tokens for external APIs | API acts as secure proxy, tokens stay server-side |
| **Tenant isolation** | BFF must resolve and enforce tenant DB | API enforces tenant data isolation |
| **Circuit restart** | BFF reconnects to DB directly | BFF reconnects to API (stateless) |
| **Horizontal scale** | BFF instances need DB connection pooling per tenant | BFF instances just make HTTP calls to API |

### 7.2 BFF API Client

```csharp
// IBffApiClient.cs — Interface in the BFF
public interface IBffApiClient
{
    Task<TResponse> GetAsync<TResponse>(string path, CancellationToken ct);
    Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct);
    Task<TResponse> PutAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
}

// BffApiClient.cs — Implementation
public sealed class BffApiClient : IBffApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ITenantContext _tenant;

    public BffApiClient(HttpClient httpClient, ITenantContext tenant)
    {
        _httpClient = httpClient;
        _tenant = tenant;
    }

    public async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Tenant-Id", _tenant.TenantId);
        request.Headers.Add("Authorization", $"Bearer {await GetDelegatedTokenAsync()}");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TResponse>(ct)
            ?? throw new InvalidOperationException("Empty API response");
    }

    // ... PostAsync, PutAsync, DeleteAsync follow same pattern

    private async Task<string> GetDelegatedTokenAsync()
    {
        // OAuth 2.0 On-Behalf-Of flow
        // The user's token is exchanged for a token scoped to the API app
        var userToken = await _tokenAcquisition.GetAccessTokenForUserAsync(
            new[] { "api://your-api-app/access" });
        return userToken;
    }
}

// Registration in Program.cs (BFF)
builder.Services.AddHttpClient<IBffApiClient, BffApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BffApi:BaseUrl"]
        ?? "http://localhost:5001");
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

### 7.3 API Endpoint Design

The API app should expose endpoints that workflows and capabilities can call:

```csharp
// API Controller Pattern
[ApiController]
[Route("api/{tenantId}/[controller]")]
public class TicketsController : ControllerBase
{
    private readonly TenantDbContextFactory _dbFactory;

    [HttpGet("open")]
    public async Task<ActionResult<IReadOnlyList<TicketDto>>> GetOpenTickets(
        string tenantId,
        [FromQuery] int days = 7,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync<WorkflowDbContext>(ct);
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var tickets = await db.Tickets
            .Where(t => t.CreatedAt >= cutoff && t.Status != "Closed")
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Select(t => new TicketDto(
                t.Id,
                t.Subject,
                t.Team,
                t.Priority,
                (int)(DateTime.UtcNow - t.CreatedAt).TotalDays,
                t.WaitingOnReply,
                t.EscalationRisk
            ))
            .ToListAsync(ct);

        return Ok(tickets);
    }

    [HttpPost("{ticketId}/draft")]
    public async Task<ActionResult<DraftResultDto>> DraftReply(
        string tenantId,
        string ticketId,
        DraftRequestDto request,
        CancellationToken ct)
    {
        // Business logic — validate, create draft, return result
        // ...
    }
}
```

### 7.4 Workflow Service Pattern with BFF Calls

```csharp
// In the BFF: SupportInboxWorkflowService.cs
public sealed class SupportInboxWorkflowService
{
    private readonly IBffApiClient _api;

    public SupportInboxWorkflowService(IBffApiClient api)
    {
        _api = api;
    }

    public async Task<string> FocusOpenTicketsAsync(int days)
    {
        var tickets = await _api.GetAsync<List<TicketDto>>(
            $"/api/tickets/open?days={days}", CancellationToken.None);

        _visibleTickets = tickets;
        _highlightedTicketIds = tickets
            .Where(t => t.EscalationRisk || t.AgeDays > 5)
            .Select(t => t.Id)
            .ToHashSet();

        return $"Found {tickets.Count} open tickets, {_highlightedTicketIds.Count} need attention.";
    }
}
```

---

## 8. AgentBlazor Configuration Pipeline

### 8.1 Full Registration from the Demo

```csharp
// Derived from AgentBlazor.Demo/Program.cs
builder.Services.AddAgentBlazor(options =>
{
    // ── STEP 1: AI Provider ──────────────────────────────────
    // In multi-tenant, this is handled by TenantAwareRuntimeAdapter
    // or a Scoped IChatClient (see §4.3)

    // ── STEP 2: Pro License (optional) ──────────────────────
    if (!string.IsNullOrWhiteSpace(proLicenseKey))
    {
        options.UseProLicense(proLicenseKey, proDataDirectory);
    }

    // ── STEP 3: Middleware Pipeline ──────────────────────────
    // Order matters — outermost runs first
    options.UseMiddleware<TenantContextEnrichmentMiddleware>();   // Sets tenant in Items
    options.UseMiddleware<TenantCostControlMiddleware>();         // Budget check
    options.UseMiddleware<TenantAuditLoggingMiddleware>();        // Per-tenant audit
    options.UseMiddleware<TenantChatRequestLoggingMiddleware>();  // JSONL log

    // ── STEP 4: DevTools ─────────────────────────────────────
    if (builder.Environment.IsDevelopment())
    {
        options.UseDevTools();
    }

    // ── STEP 5: Configure Builder (Agents + Workflows) ──────
    options.ConfigureBuilder(agentBuilder =>
    {
        // Enable prompt tracing for debugging
        agentBuilder.EnablePromptTracing();

        // Register data schemas for agent context
        agentBuilder.AddDataSchema(new AgentDataSchemaSet
        {
            Name = "support-data",
            Description = "Support ticket fields",
            Entities = { /* ... */ }
        });

        // Register agents
        agentBuilder.AddAgent("Workflow Hub Agent", agent =>
        {
            agent.WithDescription("Routes users toward the right workflow");
            agent.WithInstructions(sharedAgentInstructions);
        });

        // Register workflows
        agentBuilder.AddWorkflow<SupportInboxCapabilities>(
            "Support Inbox Agent", agent =>
        {
            agent.WithDescription("Handle support ticket triage and replies");
            agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
            agent.WithDataSchemas("support-data");
            agent.WithRoutePrefixes("/workflows/support-inbox");
        });

        // ... more workflows
    });
});

// ── STEP 6: Map Endpoints ────────────────────────────────────
app.MapAgentBlazorEndpoints();
```

### 8.2 Multi-Tenant Registration Extension

```csharp
// Extension method for cleaner startup
public static AgentBlazorBuilder AddTenantAwareAgentBlazor(
    this IServiceCollection services,
    IConfiguration configuration,
    Action<AgentBlazorRegistrationOptions>? configure = null)
{
    // Register tenant infrastructure
    services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
    services.AddSingleton<ITenantChatClientFactory, CachedTenantChatClientFactory>(
        sp => new CachedTenantChatClientFactory(
            new TenantChatClientFactory(sp.GetRequiredService<IHttpClientFactory>())));
    services.AddScoped<IBffApiClient, BffApiClient>();

    // Register AgentBlazor
    services.AddAgentBlazor(options =>
    {
        options.UseRuntimeAdapter(sp =>
        {
            var factory = sp.GetRequiredService<ITenantChatClientFactory>();
            var tenant = sp.GetRequiredService<ITenantContext>();
            var chatClient = factory.CreateClient(tenant);
            var runtimeAdapter = ActivatorUtilities.CreateInstance<ChatClientRuntimeAdapter>(
                sp, chatClient);
            return runtimeAdapter;
        });

        configure?.Invoke(options);
    });

    return builder;
}
```

---

## 9. Middleware Stack

The Demo app demonstrates a layered middleware approach. In production multi-tenant, the stack is:

### 9.1 ASP.NET Core Middleware Pipeline (order matters)

```csharp
// Program.cs — middleware pipeline
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// 1. Tenant Resolution (must run early)
app.UseMiddleware<TenantResolutionMiddleware>();  // Sets HttpContext.Items["TenantContext"]

// 2. Forwarded Headers (if behind reverse proxy)
if (tenantOptions.TrustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

// 3. Traffic Monitoring
app.UseMiddleware<TenantTrafficLoggingMiddleware>();

// 4. Status Code Pages
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// 5. Security
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAntiforgery();

// 6. Static Assets + Blazor
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// 7. AgentBlazor Endpoints (AG-UI)
app.MapAgentBlazorEndpoints();
```

### 9.2 AgentBlazor `IAgentTurnMiddleware` Pipeline

This pipeline runs inside each agent turn (each chat message):

```
Outer Middleware (registers first)
│
├─ TenantContextEnrichmentMiddleware
│   Injects TenantId, Plan, Provider info into AgentTurnContext.Items
│
├─ TenantCostControlMiddleware
│   Checks daily/monthly budget before execution
│
├─ TenantAuditLoggingMiddleware
│   Records full turn for compliance/audit
│
├─ TenantChatRequestLoggingMiddleware
│   Logs to per-tenant JSONL file (like DemoChatRequestLoggingMiddleware)
│
├─ [Inner] AgentRuntime / LLM Call
│

Inner returns → response flows back through each middleware
```

### 9.3 Middleware Registration Pattern

```csharp
// TenantContextEnrichmentMiddleware.cs
public sealed class TenantContextEnrichmentMiddleware : IAgentTurnMiddleware
{
    private readonly ITenantContextAccessor _tenantAccessor;

    public TenantContextEnrichmentMiddleware(ITenantContextAccessor tenantAccessor)
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
            context.Items["TenantPlan"] = tenant.Plan.ToString();
            context.Items["TenantProvider"] = tenant.Provider.ProviderType;
        }
        await next(ct);
    }
}

// Registration
builder.Services.AddAgentBlazor(options =>
{
    options.UseMiddleware<TenantContextEnrichmentMiddleware>();
    options.UseMiddleware<TenantCostControlMiddleware>();
    // etc.
});
```

---

## 10. Routing & Layout Architecture

### 10.1 Route Design

Based on the Demo app's route patterns:

```
/                                   → Landing page (no tenant required)
/{tenant}                           → Tenant home / dashboard
/{tenant}/workflows/support-inbox   → Support inbox workflow
/{tenant}/workflows/{workflow-name} → Other workflows
/{tenant}/components                → Component reference
/{tenant}/settings                  → Tenant configuration UI
/docs                               → Global documentation
/not-found                          → 404 page
```

Or with subdomain resolution:

```
{tenant}.yourapp.com                 → Tenant home
{tenant}.yourapp.com/workflows/...   → Workflows
```

### 10.2 Layout Strategy

Derived from the Demo's 3-layout architecture:

```
┌────────────────────────────────────────────┐
│ LandingLayout                              │
│ (unauthenticated, no tenant)               │
│ Header → Hero → Footer                     │
│ Pages: /                                   │
└────────────────────────────────────────────┘

┌────────────────────────────────────────────┐
│ TenantLayout                               │
│ (authenticated, tenant resolved)           │
│                                            │
│ Header: Brand + Tenant Name + Nav          │
│ ┌──────────────────────────┬──────────────┐│
│ │ Main Content             │ AgentChat    ││
│ │ (Workflow page,          │ Surface      ││
│ │  dashboard, settings)    │ (sidebar)    ││
│ └──────────────────────────┴──────────────┘│
│ Footer                                      │
│                                             │
│ AgentChatWidget (floating, optional)        │
└────────────────────────────────────────────┘

┌────────────────────────────────────────────┐
│ DocsLayout                                 │
│ (no tenant required)                       │
│ Header → Sidebar (nav) → Content           │
│ Pages: /docs/*                             │
└────────────────────────────────────────────┘
```

### 10.3 TenantLayout Key Features

```razor
@* TenantLayout.razor — simplified from DemoLayout *@
@inherits LayoutComponentBase
@implements IDisposable

<MudThemeProvider Theme="_theme" IsDarkMode="true" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<div class="tenant-shell">
    <header class="tenant-shell__header">
        <a href="/" class="tenant-shell__brand">
            <span class="tenant-shell__brand-name">@TenantName</span>
        </a>
        <nav class="tenant-shell__nav">
            <a href="/@TenantId/workflows">Workflows</a>
            <a href="/@TenantId/settings">Settings</a>
        </nav>
        <div class="tenant-shell__tenant-badge">
            <MudChip T="string" Color="Color.Primary" Size="Size.Small">
                @TenantPlan
            </MudChip>
        </div>
    </header>

    <div class="tenant-shell__frame @(ShowAssistant ? "tenant-shell__frame--split" : null)">
        <main class="tenant-shell__main">
            @Body
        </main>
        @if (ShowAssistant)
        {
            <aside class="tenant-shell__assistant">
                <AgentChatSurface ... />
            </aside>
        }
    </div>
</div>

@code {
    [Parameter] public string TenantId { get; set; } = "";
    [CascadingParameter] private ITenantContext? Tenant { get; set; }
    private string TenantName => Tenant?.Metadata.GetValueOrDefault("Name") ?? TenantId;
    private string TenantPlan => Tenant?.Plan.ToString() ?? "Free";
    private bool ShowAssistant => Tenant is not null;
    // ...
}
```

---

## 11. Workflow Capability Patterns

### 11.1 Standard Workflow Architecture (from Demo)

```
┌──────────────────────────────────────────────────────┐
│ Layer 1: [AgentCapability] class                     │
│   ┌──────────────────────────────────────────────┐   │
│   │ [AgentAction("Describe action")]             │   │
│   │ [AgentParam("Parameter description")]        │   │
│   │ CapabilityResult with Outputs, Warnings,     │   │
│   │   NextActions, RequiresApproval               │   │
│   └──────────────────────────────────────────────┘   │
├──────────────────────────────────────────────────────┤
│ Layer 2: Workflow Service (state + business logic)   │
│   ┌──────────────────────────────────────────────┐   │
│   │ Holds in-memory state (List, Dictionary)     │   │
│   │ event Action? Changed → notifies UI           │   │
│   │ Calls IBffApiClient for data access           │   │
│   └──────────────────────────────────────────────┘   │
├──────────────────────────────────────────────────────┤
│ Layer 3: Blazor Page (.razor)                        │
│   ┌──────────────────────────────────────────────┐   │
│   │ @page "/{tenant}/workflows/..."              │   │
│   │ Hero + summary + WorkflowDecisionSupport      │   │
│   │ AgentDataGrid / AgentDialog / AgentForm       │   │
│   │ Manual action buttons for keyboard testing    │   │
│   └──────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────┘
```

### 11.2 Multi-Tenant Capability Example

```csharp
[AgentCapability("support_inbox", Name = "Support Inbox",
    Description = "Review open tickets, draft replies, escalate")]
internal sealed class SupportInboxCapabilities
{
    private readonly SupportInboxWorkflowService _workflow;
    private readonly ITenantContext _tenant;

    public SupportInboxCapabilities(
        SupportInboxWorkflowService workflow,
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
        var summary = await _workflow.FocusOpenTicketsAsync(days);
        return CapabilityResult.Success(summary) with
        {
            Outputs = new Dictionary<string, object?>
            {
                ["tenantId"] = _tenant.TenantId,
                ["days"] = days,
                ["ticketCount"] = _workflow.VisibleTickets.Count
            }
        };
    }
}
```

### 11.3 Capability Result Handling

The Demo uses rich `CapabilityResult` patterns — essential for multi-tenant UX:

```csharp
// Success with guidance
CapabilityResult.Success("Found 7 tickets needing attention")
    .WithNextActions("Draft a reply for TCK-1042", "Escalate the blocked tickets");

// Blocked with recovery path
CapabilityResult.Blocked("Cannot prepare draft — manual review needed")
    .WithWarning("Apply the recovery playbook first")
    .WithNextActions("Apply recovery playbook");

// Invalid arguments with structured error
CapabilityResult.InvalidArguments("Review window must be 1-30 days")
    .WithOutput("parameterName", "days")
    .WithOutput("expectedShape", "integer 1-30");
```

---

## 12. Observability & Monitoring

### 12.1 Per-Tenant Logging

From the Demo's JSONL logging pattern, extended for multi-tenancy:

```
Logs/{TenantId}/
├── chat-requests.jsonl       # Per-tenant agent turn log
├── traffic-requests.jsonl    # Per-tenant HTTP traffic log
├── cost-records.jsonl        # Per-tenant cost accounting
└── errors.jsonl              # Per-tenant error log
```

Or for centralized logging (production):

| Store | Tool |
|---|---|
| **Application Insights** | `TelemetryClient.TrackEvent("AgentTurn", properties)` with `TenantId` dimension |
| **OpenTelemetry** | `ActivitySource` with `tenant.id` attribute on spans |
| **Structured logs** | Serilog with `{TenantId}` enricher |
| **Metrics** | Prometheus counters per tenant for tokens, costs, turn latency |

### 12.2 OpenTelemetry Setup

```csharp
// Program.cs
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("AgentBlazor.Core")
        .AddMeter("YourApp.Bff")
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("AgentBlazor.Core")
        .AddSource("YourApp.Bff")
        .AddOtlpExporter());
```

### 12.3 Key Metrics to Track

| Metric | Type | Labels | Purpose |
|---|---|---|---|
| `agentblazor.turns.total` | Counter | `tenant_id`, `status`, `agent_name` | Total agent turns |
| `agentblazor.tokens.total` | Counter | `tenant_id`, `type` (prompt/completion) | Token consumption |
| `agentblazor.cost.total` | Counter | `tenant_id` | Running cost tracking |
| `agentblazor.turn.duration` | Histogram | `tenant_id`, `agent_name` | Response time |
| `agentblazor.budget.exceeded` | Counter | `tenant_id`, `period` (daily/monthly) | Budget threshold alerts |

---

## 13. Deployment Pipeline

### 13.1 Docker Multi-Stage Build

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props NuGet.Config ./
COPY src/ YourApp.Shared/ src/YourApp.Shared/
COPY src/ YourApp.Bff/ src/YourApp.Bff/
COPY src/ YourApp.Api/ src/YourApp.Api/

RUN dotnet restore src/YourApp.Bff/YourApp.Bff.csproj
RUN dotnet publish src/YourApp.Bff/YourApp.Bff.csproj \
    --configuration Release --no-restore --output /app/bff /p:UseAppHost=false

RUN dotnet restore src/YourApp.Api/YourApp.Api.csproj
RUN dotnet publish src/YourApp.Api/YourApp.Api.csproj \
    --configuration Release --no-restore --output /app/api /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS bff
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build /app/bff .
ENTRYPOINT ["dotnet", "YourApp.Bff.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:8081 DOTNET_EnableDiagnostics=0
EXPOSE 8081
COPY --from=build /app/api .
ENTRYPOINT ["dotnet", "YourApp.Api.dll"]
```

### 13.2 Azure Container Apps / Kubernetes

```yaml
# Kubernetes deployment manifest (simplified)
apiVersion: apps/v1
kind: Deployment
metadata:
  name: your-app-bff
spec:
  replicas: 3
  selector:
    matchLabels: { app: bff }
  template:
    metadata:
      labels: { app: bff }
    spec:
      containers:
      - name: bff
        image: yourapp/bff:latest
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: Production
        - name: BffApi__BaseUrl
          value: http://your-app-api:8081
        - name: TenantConfigurationStore__ConnectionString
          valueFrom:
            secretKeyRef:
              name: tenant-config-store
              key: connection-string
        - name: Redis__ConnectionString
          valueFrom:
            secretKeyRef:
              name: redis
              key: connection-string
        ports:
        - containerPort: 8080
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: your-app-api
spec:
  replicas: 3
  selector:
    matchLabels: { app: api }
  template:
    metadata:
      labels: { app: api }
    spec:
      containers:
      - name: api
        image: yourapp/api:latest
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: Production
        - name: TenantConfigurationStore__ConnectionString
          valueFrom:
            secretKeyRef:
              name: tenant-config-store
              key: connection-string
        ports:
        - containerPort: 8081
---
apiVersion: v1
kind: Service
metadata:
  name: your-app-api
spec:
  selector: { app: api }
  ports:
  - port: 8081
---
apiVersion: v1
kind: Service
metadata:
  name: your-app-bff
spec:
  selector: { app: bff }
  ports:
  - port: 443
    targetPort: 8080
```

### 13.3 Tenant Provisioning CI/CD

Each new tenant triggers:

```
1. Admin creates tenant in TenantConfigurationStore
2. CI/CD pipeline:
   a. Run EF Core migrations against new tenant database
   b. Seed initial data
   c. Provision Azure OpenAI deployment (if tenant-requested)
   d. Set up per-tenant log storage
   e. Send welcome notification
3. Tenant is active
```

---

## 14. Production Readiness Checklist

### 14.1 Foundation

- [ ] **Tenant Resolution** — Middleware that resolves tenant identity from subdomain, header, cookie, or path prefix
- [ ] **Scoped `ITenantContext`** — Per-circuit tenant context that survives SignalR reconnections
- [ ] **Tenant Configuration Store** — Secure, fast-access store for per-tenant settings (connection strings, provider configs, budgets)
- [ ] **Global `ITenantConfigurationStore`** — Interface with caching for tenant metadata lookups

### 14.2 LLM Provider (derived from Demo patterns)

- [ ] **Per-tenant provider configuration** — Each tenant can choose OpenAI, Azure OpenAI, or Ollama
- [ ] **Tenant-aware `IChatClient` factory** — Creates provider instances per tenant, with caching
- [ ] **Provider fallback** — If tenant's provider fails, fall back to platform-default or show clear error
- [ ] **API key security** — Encrypted at rest, never logged, loaded via Key Vault or encrypted store
- [ ] **Azure Managed Identity support** — For Azure OpenAI, use `DefaultAzureCredential` instead of API keys where possible

### 14.3 Cost Control (extending Demo's cost tracking)

- [ ] **Per-tenant daily budget** — Hard cap on token spend per day per tenant
- [ ] **Per-tenant monthly budget** — Hard cap on token spend per month per tenant
- [ ] **Token pricing configuration** — Configurable `InputTokenCostPerMillion` and `OutputTokenCostPerMillion` per tenant
- [ ] **Real-time budget enforcement** — `IAgentTurnMiddleware` checks budget before each LLM call
- [ ] **Budget exceeded response** — Friendly message explaining the block with recovery instructions
- [ ] **Cost accounting store** — Redis for real-time counters, SQL Server for audit trail
- [ ] **Cost warning thresholds** — Log warnings at 80%, 90%, 100% of daily budget
- [ ] **Notification integration** — Email or webhook when tenant approaches or exceeds budget
- [ ] **Plan-based defaults** — Free/Pro/Enterprise tiers with appropriate budget defaults

### 14.4 SQL Server Database (replacing Demo's SQLite)

- [ ] **Per-tenant connection strings** — Stored in `TenantConfigurationStore`, loaded at runtime
- [ ] **`TenantDbContextFactory`** — Creates scoped `DbContext` with correct connection string per tenant
- [ ] **EF Core migrations** — Automated migration execution per tenant database
- [ ] **Connection resiliency** — `EnableRetryOnFailure(3)` with exponential backoff
- [ ] **Database provisioning** — Automated new tenant database creation with migrations
- [ ] **Rolling migration strategy** — Queue-based migration processing for hundreds of tenants
- [ ] **Read replicas** (Enterprise) — Read/write splitting for high-scale tenants

### 14.5 BFF Integration

- [ ] **`IBffApiClient`** — Typed `HttpClient` wrapper with tenant header injection
- [ ] **Token delegation** — OAuth 2.0 On-Behalf-Of flow from BFF to API
- [ ] **Tenant header propagation** — `X-Tenant-Id` header on every API call
- [ ] **API error handling** — Structured error responses, retry with backoff, circuit breaker
- [ ] **BFF health checks** — `/health` endpoint that verifies connectivity to API and Redis

### 14.6 Security (extending Demo's patterns)

- [ ] **Rate limiting** — Per-tenant + per-IP rate limiting (from Demo's `System.Threading.RateLimiting`)
- [ ] **Forwarded headers** — `XForwardedFor | XForwardedProto` trust for reverse proxy
- [ ] **Delegated authentication** — BFF authenticates user, exchanges token for API access
- [ ] **API authentication** — JWT bearer token validation on API endpoints
- [ ] **Tenant isolation enforcement** — Middleware on API that rejects cross-tenant data access
- [ ] **CORS** — Proper CORS configuration if API is accessed from different origins
- [ ] **Antiforgery** — Blazor's built-in antiforgery enabled (`app.UseAntiforgery()`)
- [ ] **HTTPS** — Enforced in production
- [ ] **HSTS** — `app.UseHsts()` with appropriate max-age

### 14.7 Observability (from Demo's logging system)

- [ ] **Per-tenant chat request logging** — JSONL or Application Insights, with `TenantId` dimension
- [ ] **Per-tenant traffic logging** — HTTP request logging with visitor fingerprinting (PII-hashed)
- [ ] **Structured logging** — Serilog/OpenTelemetry with `TenantId` enricher
- [ ] **Token usage tracking** — Log prompt_tokens, completion_tokens, estimated_cost per turn
- [ ] **Performance metrics** — Agent turn duration, provider latency, DB query time
- [ ] **Health check endpoint** — `/health` with detailed component status (Provider, DB, Redis, API)
- [ ] **Alerting** — Budget threshold, provider failure, high latency alerts
- [ ] **Dashboard** — Per-tenant usage dashboard (tokens, cost, active users)

### 14.8 Middleware Pipeline (from Demo's layered middleware)

- [ ] **Exception handler** — `UseExceptionHandler("/Error")` with createScopeForErrors
- [ ] **Traffic logging middleware** — Standard ASP.NET middleware for HTTP traffic
- [ ] **Agent turn middleware** — `IAgentTurnMiddleware` implementations for:
  - Tenant context enrichment
  - Cost control enforcement
  - Chat request logging
  - Audit logging
- [ ] **Rate limiter** — `UseRateLimiter()` with per-tenant policy
- [ ] **Status code pages** — `UseStatusCodePagesWithReExecute()`
- [ ] **Forwarded headers** — Behind reverse proxy

### 14.9 Blazor UI & Components

- [ ] **Layout architecture** — LandingLayout (public) + TenantLayout (authenticated) + DocsLayout
- [ ] **AgentChatSurface vs AgentChatWidget** — Sidebar for workflow pages, floating widget for others (like Demo)
- [ ] **WorkflowDecisionSupport component** — Shared component showing phase, blockers, warnings, next actions
- [ ] **Approval dialog** — `AgentDialog` for `RequiresApproval = true` boundaries
- [ ] **Client ID management** — Session-persistent client ID via JS interop (from Demo's `demo-session.js`)
- [ ] **Reconnect modal** — Interactive Server reconnection UX
- [ ] **CSS design tokens** — `:root` variables for theming, BEM methodology
- [ ] **Responsive layout** — Collapse assistant pane below 1180px

### 14.10 Agent & Workflow Design (from Demo's patterns)

- [ ] **Shared agent instructions** — System prompt loaded from file, injectable into all agents
- [ ] **Capability-based design** — `[AgentCapability]` classes with `[AgentAction]` methods
- [ ] **Rich `CapabilityResult`** — Use `Success`, `Blocked`, `InvalidArguments`, `NeedsClarification`
- [ ] **Approval boundaries** — `RequiresApproval = true` on mutation actions
- [ ] **Structured error responses** — `WithOutput("errorCode", ...)` with recovery hints
- [ ] **Data schemas** — `AgentDataSchemaSet` for entity-aware agents
- [ ] **Route prefix binding** — `WithRoutePrefixes()` to scope agents to routes
- [ ] **Allowed components** — `WithAllowedComponents()` to restrict agent access
- [ ] **Cross-workflow orchestration** — Query-param-based guided journeys with return-state tracking

### 14.11 Deployment & DevOps

- [ ] **Docker multi-stage build** — Separate images for BFF and API
- [ ] **SignalR scaling** — Azure SignalR Service or Redis backplane for multi-instance BFF
- [ ] **Redis instance** — For cost counters, tenant config caching, session state
- [ ] **Tenant Configuration Store** — Database or Key Vault-backed tenant settings
- [ ] **CI/CD pipeline** — Automated build, test, and deployment
- [ ] **Database migrations** — Automated per-tenant migration execution
- [ ] **Health probes** — Liveness + readiness probes for both BFF and API
- [ ] **Zero-downtime deployment** — Rolling update strategy
- [ ] **Backup strategy** — Per-tenant database backup + point-in-time restore

### 14.12 Testing

- [ ] **Unit tests** — Workflow services, capability classes (following Demo's test patterns)
- [ ] **Integration tests** — BFF → API communication, tenant resolution
- [ ] **E2E tests** — Blazor component interaction with Playwright
- [ ] **Cost control tests** — Budget enforcement, budget exceeded flows
- [ ] **Multi-tenant isolation tests** — Verify Tenant A cannot access Tenant B's data
- [ ] **Provider fallback tests** — What happens when a tenant's provider is unreachable

---

## 15. Appendix: Key Code Patterns

### 15.1 Demo's `Program.cs` Startup Pattern (Adapted)

```csharp
public static void Main(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseStaticWebAssets();

    // ── Logging ──────────────────────────────────────────────
    builder.Logging.AddFilter("AgentBlazor.Core.Runtime.Agents.AgentRuntime", LogLevel.Information);
    builder.Logging.AddFilter("AgentBlazor", LogLevel.Information);

    // ── Blazor + MudBlazor ────────────────────────────────────
    builder.Services.AddRazorComponents().AddInteractiveServerComponents();
    builder.Services.AddMudServices();

    // ── Tenant Resolution ─────────────────────────────────────
    builder.Services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
    builder.Services.AddSingleton<ITenantConfigurationStore, TenantConfigurationStore>();

    // ── HTTP Clients ──────────────────────────────────────────
    builder.Services.AddHttpClient<IBffApiClient, BffApiClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["BffApi:BaseUrl"]!);
    });
    builder.Services.AddHttpClient(); // For TenantChatClientFactory

    // ── Configuration ─────────────────────────────────────────
    builder.Services.Configure<TenantCostControlOptions>(builder.Configuration.GetSection("CostControl"));
    builder.Services.Configure<GlobalSecurityOptions>(builder.Configuration.GetSection("Security"));

    // ── AgentBlazor ────────────────────────────────────────────
    var sharedInstructionsPath = Path.Combine(builder.Environment.ContentRootPath, "agent-instructions.txt");
    var sharedInstructions = File.Exists(sharedInstructionsPath)
        ? File.ReadAllText(sharedInstructionsPath) : null;

    builder.Services.AddTenantAwareAgentBlazor(builder.Configuration, options =>
    {
        // License
        var licenseKey = builder.Configuration["AgentBlazor:LicenseKey"];
        if (!string.IsNullOrWhiteSpace(licenseKey))
        {
            options.UseProLicense(licenseKey, "/data/agentblazor-pro");
        }

        // Middleware (order matters)
        options.UseMiddleware<TenantCostControlMiddleware>();
        options.UseMiddleware<TenantChatRequestLoggingMiddleware>();
        options.UseMiddleware<TenantAuditLoggingMiddleware>();

        if (builder.Environment.IsDevelopment())
        {
            options.UseDevTools();
        }

        // Builder configuration
        options.ConfigureBuilder(agentBuilder =>
        {
            agentBuilder.EnablePromptTracing();

            // Shared agent
            agentBuilder.AddAgent("Platform Agent", agent =>
            {
                agent.WithDescription("Focused on routing users toward the right workflow.");
                if (sharedInstructions is not null) agent.WithInstructions(sharedInstructions);
            });

            // Workflows
            agentBuilder.AddWorkflow<SupportInboxCapabilities>("Support Inbox Agent", agent =>
            {
                agent.WithDescription("Handle support ticket triage and replies.");
                agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
                agent.WithRoutePrefixes("/workflows/support-inbox");
            });

            // ... more workflows
        });
    });

    // ── Build ──────────────────────────────────────────────────
    var app = builder.Build();

    // ── Middleware Pipeline ────────────────────────────────────
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseHttpsRedirection();
    app.UseAntiforgery();
    app.MapStaticAssets();
    app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    app.MapAgentBlazorEndpoints();

    app.Run();
}
```

### 15.2 Example `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "AgentBlazor": "Information"
    }
  },
  "AllowedHosts": "*",
  "BffApi": {
    "BaseUrl": "http://localhost:5001"
  },
  "Security": {
    "TrustForwardedHeaders": true,
    "RateLimiting": {
      "Enabled": true,
      "DefaultPermitLimit": 20,
      "WindowSeconds": 60,
      "QueueLimit": 0
    },
    "EnterprisePermitLimit": 100
  },
  "CostControl": {
    "Enabled": true,
    "DefaultDailyBudgetUsd": 5.00,
    "DefaultMonthlyBudgetUsd": 100.00,
    "DefaultInputTokenCostPerMillion": 0.15,
    "DefaultOutputTokenCostPerMillion": 0.60
  },
  "TenantConfigurationStore": {
    "Provider": "SqlServer",
    "ConnectionString": ""
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
}
```

### 15.3 Tenant Resolution Middleware (ASP.NET Core)

```csharp
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor accessor)
    {
        var tenantId = ResolveTenantId(context);
        if (tenantId is not null)
        {
            var configStore = context.RequestServices.GetRequiredService<ITenantConfigurationStore>();
            var config = await configStore.GetTenantConfigAsync(tenantId, context.RequestAborted);
            if (config is not null)
            {
                accessor.TenantContext = new TenantContext(tenantId, config);
            }
        }
        await _next(context);
    }

    private static string? ResolveTenantId(HttpContext context)
    {
        // 1. Subdomain: tenant.yourapp.com
        var host = context.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(host) && host.Contains('.') && !host.StartsWith("www."))
        {
            return host.Split('.')[0];
        }

        // 2. Header (set by API gateway)
        var header = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header)) return header;

        // 3. Cookie (for SignalR circuits)
        var cookie = context.Request.Cookies["TenantId"];
        if (!string.IsNullOrWhiteSpace(cookie)) return cookie;

        return null;
    }
}
```

### 15.4 Tenant-aware `IChatClient` Resolution

```csharp
// Registration
builder.Services.AddScoped<IChatClient>(sp =>
{
    var tenant = sp.GetRequiredService<ITenantContext>();
    var factory = sp.GetRequiredService<ITenantChatClientFactory>();
    return factory.CreateClient(tenant);
});

// AgentBlazor will pick this up from DI when creating the runtime adapter.
// If the adapter expects a singleton IChatClient, you'll need to use
// options.UseRuntimeAdapter() instead and create the client inside the adapter.
```

---

## References

- `AgentBlazor.Demo` — `demo/AgentBlazor.Demo/Program.cs` (full 450-line startup)
- `AgentBlazor.Demo` — `Services/DemoChatRequestLoggingMiddleware.cs` (IAgentTurnMiddleware pattern)
- `AgentBlazor.Hosting` — `src/AgentBlazor.Hosting/AgentBlazorRegistrationOptions.cs` (registration API)
- `AgentBlazor.Hosting` — `src/AgentBlazor.Hosting/AgentBlazorUnifiedServiceCollectionExtensions.cs` (AddAgentBlazor)
- `AgentBlazor.Core` — `src/AgentBlazor.Core/App/CapabilityResult.cs` (result pattern)
- `AgentBlazor.ProviderAdapters` — `src/AgentBlazor.ProviderAdapters/AgentProviderRegistrationExtensions.cs` (provider registration)
- `docs/quickstart.md` — Setup guide
- `docs/internal/architecture.md` — Internal architecture docs

---

> **Authored:** 2026-07-13  
> **Based on:** AgentBlazor v0.2.22, .NET 10.0.106, Blazor Interactive Server (InteractiveServer)
