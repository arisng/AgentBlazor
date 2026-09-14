# BFF API Client Pattern

## Contents

- [Why BFF for AgentBlazor?](#why-bff-for-agentblazor)
- [Typed HttpClient for BFF → API](#typed-httpclient-for-bff--api)
- [Registration](#registration)
- [API-Side Tenant Validation](#api-side-tenant-validation)
- [Using BffApiClient in Workflow Capabilities](#using-bffapiclient-in-workflow-capabilities)
- [Circuit Breaker & Retry](#circuit-breaker--retry)

The BFF (Blazor host) calls the API app for all data access. This keeps the BFF stateless and enforces tenant isolation at the API boundary.

## Why BFF for AgentBlazor?

| Concern | BFF has direct DB access | BFF calls API | 
|---|---|---|
| Tenant data isolation | BFF must enforce manually | API enforces via Finbuckle DbContext |
| Connection strings | BFF needs per-tenant connection pool | Only API needs connection strings |
| Horizontal scale | Per-instance connection pools | BFF instances make HTTP calls |
| Token security | Tokens flow to DB | Tokens stay server-side |
| Circuit restart | Reconnects to DB directly | Reconnects to stateless API |

## Typed HttpClient for BFF → API

```csharp
// Bff/Services/BffApi/IBffApiClient.cs
public interface IBffApiClient
{
    Task<T> GetAsync<T>(string path, CancellationToken ct = default);
    Task<TResponse> PostAsync<TRequest, TResponse>(
        string path, TRequest body, CancellationToken ct = default);
}

// Bff/Services/BffApi/BffApiClient.cs
public sealed class BffApiClient : IBffApiClient
{
    private readonly HttpClient _http;
    private readonly TenantContextAccessor _tenantAccessor;
    private readonly ITokenAcquisition _tokenAcquisition;

    public BffApiClient(
        HttpClient http,
        TenantContextAccessor tenantAccessor,
        ITokenAcquisition tokenAcquisition)
    {
        _http = http;
        _tenantAccessor = tenantAccessor;
        _tokenAcquisition = tokenAcquisition;
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        await ApplyHeadersAsync(request);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw new InvalidOperationException("Empty response");
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path, TRequest body, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        await ApplyHeadersAsync(request);

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(ct)
            ?? throw new InvalidOperationException("Empty response");
    }

    private async Task ApplyHeadersAsync(HttpRequestMessage request)
    {
        // Tenant identity
        var tenantId = _tenantAccessor.TenantContext?.TenantId
            ?? throw new InvalidOperationException("No tenant context");
        request.Headers.Add("X-Tenant-Id", tenantId);

        // OAuth 2.0 On-Behalf-Of flow
        var token = await _tokenAcquisition.GetAccessTokenForUserAsync(
            new[] { "api://your-api/access_as_user" });
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }
}
```

## Registration

```csharp
// In BFF Program.cs
builder.Services.AddHttpClient<IBffApiClient, BffApiClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["BffApi:BaseUrl"] ?? "http://localhost:5001");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});
```

## API-Side Tenant Validation

The API validates the `X-Tenant-Id` header matches the authenticated user's tenant:

```csharp
// In API Program.cs
builder.Services.AddMultiTenant<TenantInfo>()
    .WithResolutionStrategy<HeaderResolutionStrategy>()  // reads X-Tenant-Id
    .WithStore<EFCoreStore<TenantDbContext, TenantInfo>>(ServiceLifetime.Scoped);

// Middleware: enforce tenant matches the JWT's tenant claim
app.Use(async (context, next) =>
{
    var tenantId = context.GetMultiTenantContext<TenantInfo>()?.TenantInfo?.Identifier;
    var jwtTenantId = context.User.FindFirst("tenant_id")?.Value;

    if (tenantId != jwtTenantId)
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("Tenant mismatch.");
        return;
    }
    await next();
});
```

## Using BffApiClient in Workflow Capabilities

```csharp
[AgentCapability("support_inbox")]
internal sealed class SupportInboxCapabilities
{
    private readonly IBffApiClient _api;
    private readonly TenantContextAccessor _tenant;

    public SupportInboxCapabilities(
        IBffApiClient api, TenantContextAccessor tenant)
    {
        _api = api;
        _tenant = tenant;
    }

    [AgentAction("Show open tickets")]
    public async Task<CapabilityResult> ShowOpenTicketsAsync(
        [AgentParam(Required = false)] int days = 7)
    {
        var tickets = await _api.GetAsync<List<TicketDto>>(
            $"/api/tickets/open?days={days}");

        return CapabilityResult.Success($"Found {tickets.Count} open tickets")
            .WithOutput("ticketCount", tickets.Count)
            .WithOutput("tenantId", _tenant.TenantContext?.TenantId);
    }
}
```

## Circuit Breaker & Retry

Add resilience for production:

```csharp
builder.Services.AddHttpClient<IBffApiClient, BffApiClient>(client =>
{
    client.BaseAddress = new Uri(config["BffApi:BaseUrl"]!);
})
.AddTransientHttpErrorPolicy(policy =>
    policy.WaitAndRetryAsync(3, retry => TimeSpan.FromMilliseconds(100 * Math.Pow(2, retry))))
.AddCircuitBreakerPolicy(policy =>
    policy.Handle<HttpRequestException>()
          .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));
```

Requires `Microsoft.Extensions.Http.Polly`.
