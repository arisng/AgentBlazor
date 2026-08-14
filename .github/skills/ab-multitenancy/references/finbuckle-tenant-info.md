# Finbuckle Tenant Info

## Contents

- [Finbuckle Tenant Info](#finbuckle-tenant-info)
  - [Contents](#contents)
  - [Entity Definition](#entity-definition)
  - [Finbuckle Store Registration](#finbuckle-store-registration)
  - [Tenant DbContext (for the configuration store itself)](#tenant-dbcontext-for-the-configuration-store-itself)
  - [Tenant Resolution + AsyncLocal Hydration](#tenant-resolution--asynclocal-hydration)
  - [API Key Security](#api-key-security)

The `TenantInfo` entity holds every tenant-specific setting — LLM provider, connection string, cost budgets — in a single configuration store. Finbuckle resolves this per request and you hydrate it into `ITenantContext` for AgentBlazor.

## Entity Definition

> **Note on explicit interface members:** `TenantId` and `TenantName` are implemented explicitly (`string ITenantContext.TenantId`). This means `tenantInfo.TenantId` does not compile — you must cast to `ITenantContext` or access through `TenantContextAccessor.TenantContext` (which returns `ITenantContext`). All other members (`ProviderType`, `Model`, budgets) are implicit public properties and accessible directly.

```csharp
using Finbuckle.MultiTenant.Abstractions;

public sealed class TenantInfo : ITenantInfo, ITenantContext
{
    // ── Finbuckle identity ──
    public string Id { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ConnectionString { get; set; }

    // ── ITenantContext members ──
    string ITenantContext.TenantId => Identifier;
    string? ITenantContext.TenantName => Name;

    // ── LLM Provider ──
    public string ProviderType { get; set; } = "OpenAI";
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public string? AzureDeploymentName { get; set; }

    // ── Cost Control ──
    public decimal DailyBudgetUsd { get; set; } = 5.00m;
    public decimal MonthlyBudgetUsd { get; set; } = 100.00m;
    public decimal InputTokenCostPerMillion { get; set; } = 0.15m;
    public decimal OutputTokenCostPerMillion { get; set; } = 0.60m;
    public bool HardCapEnabled { get; set; } = true;

    // ── Tier ──
    public string Tier { get; set; } = "Free";  // Free, Pro, Enterprise
    public int MaxTurnsPerSession { get; set; } = 50;
    public int MaxHistoryInPrompt { get; set; } = 5;
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromHours(24);
}
```

## Finbuckle Store Registration

Use a SQL-backed store so tenant configs are centrally managed:

```csharp
// In Program.cs
builder.Services.AddMultiTenant<TenantInfo>()
    .WithStore<EFCoreStore<TenantDbContext, TenantInfo>>(ServiceLifetime.Scoped)
    .WithResolutionStrategy<HostResolutionStrategy>()     // subdomain
    .WithResolutionStrategy<HeaderResolutionStrategy>()   // X-Tenant-Id header
    .WithResolutionStrategy<CookieResolutionStrategy>()   // TenantId cookie (for SignalR)
    .WithPerTenantConnectionString(options =>
    {
        options.DefaultConnectionString =
            builder.Configuration.GetConnectionString("Shared")!;
        // Finbuckle swaps ConnectionString per tenant automatically
    });
```

## Tenant DbContext (for the configuration store itself)

```csharp
using Finbuckle.MultiTenant.EntityFrameworkCore.Stores.EFCoreStore;

public sealed class TenantDbContext : EFCoreStoreDbContext<TenantInfo>
{
    public TenantDbContext(
        DbContextOptions<TenantDbContext> options,
        ITenantInfo? currentTenant = null)
        : base(options, currentTenant) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<TenantInfo>(entity =>
        {
            entity.ToTable("Tenants");
            entity.Property(t => t.Identifier).HasMaxLength(256).IsRequired();
            entity.HasIndex(t => t.Identifier).IsUnique();
            entity.Property(t => t.ApiKey).HasMaxLength(512);  // encrypt at rest
        });
    }
}
```

## Tenant Resolution + AsyncLocal Hydration

```csharp
// After UseMultiTenant() in middleware pipeline:
app.UseMultiTenant();
app.Use(async (context, next) =>
{
    var multiTenantContext = context.GetMultiTenantContext<TenantInfo>();
    if (multiTenantContext?.TenantInfo is { } tenantInfo)
    {
        var accessor = context.RequestServices.GetRequiredService<TenantContextAccessor>();
        accessor.TenantContext = tenantInfo;
    }
    await next();
});
```

## API Key Security

Never store API keys in plaintext. Options:

1. **Encrypted column** — Encrypt at rest with `IDataProtector` or Azure Key Vault keys, decrypt on read
2. **Key Vault reference** — Store `keyvault://acme-openai-key` in the DB, resolve via `SecretClient` at runtime
3. **Azure Managed Identity** — For Azure OpenAI, skip API key entirely and use `DefaultAzureCredential`

Add a provider config resolver that decrypts/maps securely:

```csharp
public sealed class TenantConfigResolver
{
    private readonly IConfigurationStore<TenantInfo> _store;
    private readonly IDataProtector? _protector;

    public async Task<TenantInfo> ResolveAsync(string tenantId, CancellationToken ct)
    {
        var tenant = await _store.TryGetByIdentifierAsync(tenantId);
        if (tenant is null) throw new InvalidOperationException($"Tenant '{tenantId}' not found.");

        // Decrypt API key if protector is configured
        if (_protector is not null && tenant.ApiKey is not null)
            tenant.ApiKey = _protector.Unprotect(tenant.ApiKey);

        return tenant;
    }
}
```
