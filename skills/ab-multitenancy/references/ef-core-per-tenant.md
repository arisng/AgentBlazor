# Per-Tenant EF Core with Finbuckle

## Contents

- [Architecture](#architecture)
- [Application DbContext with Finbuckle](#application-dbcontext-with-finbuckle)
- [Registration](#registration)
- [Per-Tenant Connection Strings](#per-tenant-connection-strings)
- [Resolving DbContext in API Controllers](#resolving-dbcontext-in-api-controllers)
- [Migration Strategy: Database-per-Tenant](#migration-strategy-database-per-tenant)
- [Rolling Migrations for Many Tenants](#rolling-migrations-for-many-tenants)

Use Finbuckle's `MultiTenantDbContext` to automatically scope application data per tenant while keeping AgentBlazor's conversation store separate.

## Architecture

Finbuckle handles **application data** (tickets, workflows, business entities). AgentBlazor's conversation store handles **agent chat history** separately — see `tenant-conversation-store.md`.

```
                       ┌──────────────────────────┐
Application DbContext  │  Finbuckle MultiTenant   │  Automatic tenant filter
  (Tickets, Workflows) │  DbContext               │  on every query
                       └──────────────────────────┘

                       ┌──────────────────────────┐
Conversation DbContext │  Manual TenantId filter  │  Custom IConversationStore
  (Sessions, Turns)    │  via TenantContextAccessor│  with explicit filtering
                       └──────────────────────────┘
```

## Application DbContext with Finbuckle

```csharp
using Finbuckle.MultiTenant.EntityFrameworkCore;

public sealed class AppDbContext : MultiTenantDbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantInfo? currentTenant = null)
        : base(options, currentTenant) { }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    // ... other domain entities

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Entities with ITenantId are automatically filtered by Finbuckle
        builder.Entity<Ticket>(entity =>
        {
            entity.ToTable("Tickets");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Status });
        });
    }
}

// Entity must implement ITenantId:
public sealed class Ticket : ITenantId
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;  // set automatically by Finbuckle
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    // ...
}
```

## Registration

```csharp
// Shared connection string — Finbuckle swaps it per tenant at runtime
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Shared")!,
        sql => sql.EnableRetryOnFailure(3)));

// Finbuckle wires it all together
builder.Services.AddMultiTenant<TenantInfo>()
    .WithPerTenantConnectionString(options =>
    {
        options.DefaultConnectionString =
            builder.Configuration.GetConnectionString("Shared")!;
    })
    .WithEFCoreStore<TenantDbContext, TenantInfo>();
```

## Per-Tenant Connection Strings

Each `TenantInfo` row has its own `ConnectionString`. Finbuckle resolves it per request and the `DbContext` automatically connects to the correct database.

```sql
-- Example tenant config table
INSERT INTO Tenants (Id, Identifier, Name, ConnectionString, ProviderType, Model, ...)
VALUES (
  NEWID(),
  'acme-corp',
  'ACME Corporation',
  'Server=sql.database.windows.net;Database=AgentBlazor_Acme;...',
  'AzureOpenAI',
  'gpt-4o'
);
```

## Resolving DbContext in API Controllers

```csharp
// In the API app:
[ApiController]
[Route("api/[controller]")]
public class TicketsController : ControllerBase
{
    private readonly AppDbContext _db;  // tenant-filtered automatically

    public TicketsController(AppDbContext db) => _db = db;

    [HttpGet("open")]
    public async Task<ActionResult<List<TicketDto>>> GetOpenTickets(
        [FromQuery] int days = 7, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var tickets = await _db.Tickets
            .Where(t => t.CreatedAt >= cutoff && t.Status != "Closed")
            .OrderByDescending(t => t.Priority)
            .Select(t => new TicketDto(t.Id, t.Subject, t.Status, t.Priority))
            .ToListAsync(ct);
        return Ok(tickets);
    }
}
```

No manual `.Where(t => t.TenantId == ...)` — Finbuckle applies the filter automatically via `MultiTenantDbContext`.

## Migration Strategy: Database-per-Tenant

Each tenant gets its own database. Migrations run separately per database:

```csharp
// TenantProvisioningService.cs
public async Task ProvisionTenantDatabaseAsync(
    string tenantId, string connectionString, CancellationToken ct)
{
    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
    optionsBuilder.UseSqlServer(connectionString);

    await using var db = new AppDbContext(optionsBuilder.Options, null);
    await db.Database.MigrateAsync(ct);

    // Seed initial data if needed
    if (!await db.Tickets.AnyAsync(ct))
    {
        await SeedAsync(db, tenantId, ct);
    }
}
```

## Rolling Migrations for Many Tenants

For 100+ tenants, use a queue-based approach:

```csharp
public async Task MigrateAllTenantsAsync(CancellationToken ct)
{
    var tenants = await GetActiveTenantsAsync(ct);
    var options = new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct };

    await Parallel.ForEachAsync(tenants, options, async (tenant, innerCt) =>
    {
        try
        {
            await ProvisionTenantDatabaseAsync(
                tenant.Identifier, tenant.ConnectionString!, innerCt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed for tenant {Tenant}", tenant.Identifier);
            // Track failure, alert ops, retry later
        }
    });
}
```
