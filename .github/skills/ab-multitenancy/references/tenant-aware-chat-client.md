# Tenant-Aware Chat Client (Proxy IChatClient)

## Contents

- [Rationale](#rationale)
- [Full Implementation](#full-implementation)
- [Registration](#registration)
- [How It Flows](#how-it-flows)
- [Caching Strategy](#caching-strategy)

Full implementation of a singleton proxy `IChatClient` that resolves the real LLM provider per tenant on every call. This is the correct pattern for multi-tenant AgentBlazor.

## Rationale

AgentBlazor's `ChatClientRuntimeAdapter` is a singleton that stores `_chatClient` as a `readonly` field. Creating separate adapters per tenant via `UseRuntimeAdapter(factory)` is impossible — the factory runs once at startup, baking one tenant's client in forever.

The proxy pattern keeps the adapter unaware of multi-tenancy while giving each tenant its own provider.

## Full Implementation

```csharp
using Microsoft.Extensions.AI;
using OpenAI;
using Azure.AI.OpenAI;
using Azure;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

public sealed class TenantAwareChatClient : IChatClient
{
    private readonly TenantContextAccessor _tenantAccessor;
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new();

    public TenantAwareChatClient(TenantContextAccessor tenantAccessor)
        => _tenantAccessor = tenantAccessor;

    public ChatClientMetadata Metadata => new("TenantAwareChatClient");

    // ── Core: complete async ──────────────────────────

    public async Task<ChatCompletion> CompleteAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var client = GetClientForCurrentTenant();
        return await client.CompleteAsync(messages, options, ct);
    }

    // ── Streaming ─────────────────────────────────────

    public async IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteStreamingAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = GetClientForCurrentTenant();
        await foreach (var update in client.CompleteStreamingAsync(messages, options, ct))
            yield return update;
    }

    // ── Service resolution (for tools, function calling) ─

    public TService? GetService<TService>(object? key = null) where TService : class
    {
        // Forward to the current tenant's client if it supports the service
        var client = TryGetClientForCurrentTenant();
        return client?.GetService<TService>(key);
    }

    // ── Helpers ───────────────────────────────────────

    private IChatClient GetClientForCurrentTenant()
    {
        var tenant = _tenantAccessor.TenantContext
            ?? throw new InvalidOperationException(
                "No tenant context set. Ensure TenantResolutionMiddleware runs before AgentBlazor.");

        return _clients.GetOrAdd(tenant.TenantId, _ => BuildClient(tenant));
    }

    private IChatClient? TryGetClientForCurrentTenant()
    {
        var tenant = _tenantAccessor.TenantContext;
        if (tenant is null) return null;
        return _clients.GetOrAdd(tenant.TenantId, _ => BuildClient(tenant));
    }

    // ── Provider builders ─────────────────────────────

    private static IChatClient BuildClient(ITenantContext tenant) => tenant.ProviderType switch
    {
        "OpenAI" => BuildOpenAI(tenant),
        "AzureOpenAI" => BuildAzureOpenAI(tenant),
        "Ollama" => BuildOllama(tenant),
        _ => throw new NotSupportedException($"Provider '{tenant.ProviderType}' not supported.")
    };

    private static IChatClient BuildOpenAI(ITenantContext tenant)
    {
        var client = new OpenAIClient(tenant.ApiKey
            ?? throw new InvalidOperationException($"API key missing for tenant '{tenant.TenantId}'."));
        return client.GetChatClient(tenant.Model).AsIChatClient();
    }

    private static IChatClient BuildAzureOpenAI(ITenantContext tenant)
    {
        var endpoint = new Uri(tenant.Endpoint
            ?? throw new InvalidOperationException($"Endpoint missing for tenant '{tenant.TenantId}'."));
        var client = new AzureOpenAIClient(endpoint, new ApiKeyCredential(tenant.ApiKey ?? ""));
        return client.GetChatClient(tenant.AzureDeploymentName ?? tenant.Model).AsIChatClient();
    }

    private static IChatClient BuildOllama(ITenantContext tenant)
    {
        var endpoint = tenant.Endpoint ?? "http://127.0.0.1:11434/v1";
        var client = new OpenAIClient(
            new ApiKeyCredential(tenant.ApiKey ?? "ollama"),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        return client.GetChatClient(tenant.Model).AsIChatClient();
    }

    // ── Cache eviction ────────────────────────────────

    public void EvictTenant(string tenantId)
    {
        _clients.TryRemove(tenantId, out _);
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            (client as IDisposable)?.Dispose();
        _clients.Clear();
    }
}
```

## Registration

```csharp
// Singleton — matches IChatClient lifetime expectations in AgentBlazor.
builder.Services.AddSingleton<IChatClient, TenantAwareChatClient>();
```

## How It Flows

```
1. HTTP request arrives: {tenant}.yourapp.com/chat
2. Finbuckle UseMultiTenant() resolves TenantInfo
3. Middleware sets TenantContextAccessor.TenantContext (AsyncLocal)
4. User sends message → AgentChatSurface → RuntimeAdapter.RunTurnAsync()
5. ChatClientRuntimeAdapter._chatClient.CompleteAsync(...)
6. TenantAwareChatClient reads TenantContextAccessor.TenantContext
7. Gets or creates the real IChatClient for that tenant
8. Delegates the call → tenant gets its own LLM
```

## Caching Strategy

`ConcurrentDictionary<string, IChatClient>` caches clients indefinitely per tenant. For production:

- Add eviction on tenant config update
- Use `MemoryCache` with sliding expiration (e.g., 30 min)
- Invalidate cache when `ApiKey`, `Endpoint`, or `Model` changes

```csharp
private readonly MemoryCache _cache = new(new MemoryCacheOptions());
private readonly SemaphoreSlim _lock = new(1, 1);

private IChatClient GetOrCreateClient(ITenantContext tenant)
{
    if (_cache.TryGetValue(tenant.TenantId, out IChatClient? cached) && cached is not null)
        return cached;

    _lock.Wait();
    try
    {
        if (_cache.TryGetValue(tenant.TenantId, out cached) && cached is not null)
            return cached;

        var client = BuildClient(tenant);
        _cache.Set(tenant.TenantId, client, TimeSpan.FromMinutes(30));
        return client;
    }
    finally { _lock.Release(); }
}
```
