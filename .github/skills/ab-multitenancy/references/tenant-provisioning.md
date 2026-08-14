# Tenant Provisioning

## Contents

- [Provisioning Workflow](#provisioning-workflow)
- [Full Provisioning Service](#full-provisioning-service)
- [CI/CD Integration](#cicd-integration)
- [Tenant Deprovisioning](#tenant-deprovisioning)
- [Per-Tenant Plan Defaults](#per-tenant-plan-defaults)

Automate new tenant setup: database creation, migrations, seed data, and AgentBlazor configuration.

## Provisioning Workflow

```
1. Admin creates tenant in TenantConfigurationStore
2. Provisioning pipeline triggers:
   a. CREATE DATABASE [AgentBlazor_{TenantId}]
   b. Run EF Core migrations
   c. Seed initial data (roles, default workflows, etc.)
   d. Validate AgentBlazor configuration (provider connectivity)
   e. Set up per-tenant cost counters (Redis)
   f. Send welcome notification
3. Tenant goes active
```

## Full Provisioning Service

```csharp
public sealed class TenantProvisioningService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TenantConfigResolver _configResolver;
    private readonly ITenantCostStore _costStore;
    private readonly IDatabase _redis;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        IServiceScopeFactory scopeFactory,
        TenantConfigResolver configResolver,
        ITenantCostStore costStore,
        IConnectionMultiplexer redis,
        ILogger<TenantProvisioningService> logger)
    {
        _scopeFactory = scopeFactory;
        _configResolver = configResolver;
        _costStore = costStore;
        _redis = redis.GetDatabase();
        _logger = logger;
    }

    public async Task ProvisionAsync(string tenantId, CancellationToken ct)
    {
        _logger.LogInformation("Provisioning tenant {TenantId}", tenantId);

        var tenant = await _configResolver.ResolveAsync(tenantId, ct);

        // 1. Create tenant database
        await CreateDatabaseAsync(tenant, ct);

        // 2. Apply EF Core migrations
        await RunMigrationsAsync(tenant, ct);

        // 3. Seed initial data
        await SeedAsync(tenant, ct);

        // 4. Validate LLM provider connectivity
        await ValidateProviderAsync(tenant, ct);

        // 5. Initialize cost counters
        await InitializeCostCountersAsync(tenant, ct);

        _logger.LogInformation("Tenant {TenantId} provisioned successfully", tenantId);
    }

    private async Task CreateDatabaseAsync(TenantInfo tenant, CancellationToken ct)
    {
        // Connect to master and create the database
        var masterConnectionString = tenant.ConnectionString!
            .Replace($"Database=AgentBlazor_{tenant.Identifier}", "Database=master");

        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(ct);

        var dbName = $"AgentBlazor_{tenant.Identifier}";
        var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = @dbName)
            BEGIN
                CREATE DATABASE [{dbName}];
            END
            """;
        cmd.Parameters.AddWithValue("@dbName", dbName);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task RunMigrationsAsync(TenantInfo tenant, CancellationToken ct)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(tenant.ConnectionString!);

        await using var db = new AppDbContext(optionsBuilder.Options, null);
        await db.Database.MigrateAsync(ct);
    }

    private async Task SeedAsync(TenantInfo tenant, CancellationToken ct)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(tenant.ConnectionString!);

        await using var db = new AppDbContext(optionsBuilder.Options, null);

        // Idempotent seed — skip if already seeded
        if (await db.Roles.AnyAsync(ct)) return;

        db.Roles.AddRange(
            new Role { Name = "Admin", TenantId = tenant.Identifier },
            new Role { Name = "User", TenantId = tenant.Identifier }
        );
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidateProviderAsync(TenantInfo tenant, CancellationToken ct)
    {
        try
        {
            // Use a temporary TenantAwareChatClient to validate provider connectivity.
            // BuildClient is internal to TenantAwareChatClient — extract it to a static
            // helper or use the full proxy with a manually-set tenant context.
            var accessor = new TenantContextAccessor { TenantContext = tenant };
            using var proxy = new TenantAwareChatClient(accessor);
            var response = await proxy.CompleteAsync(
                [new ChatMessage(ChatRole.User, "ping")],
                new ChatOptions { MaxTokens = 5 }, ct);
            _logger.LogInformation("Provider validation for {TenantId}: OK", tenant.Identifier);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider validation failed for {TenantId}", tenant.Identifier);
            throw;
        }
    }

    private async Task InitializeCostCountersAsync(TenantInfo tenant, CancellationToken ct)
    {
        // Seed Redis counters at 0 (or whatever carry-over balance exists)
        var dateKey = $"cost:daily:{tenant.Identifier}:{DateTime.UtcNow:yyyy-MM-dd}";
        var monthKey = $"cost:monthly:{tenant.Identifier}:{DateTime.UtcNow:yyyy-MM}";
        await _redis.StringSetAsync(dateKey, 0);
        await _redis.KeyExpireAsync(dateKey, TimeSpan.FromDays(90));
        await _redis.StringSetAsync(monthKey, 0);
        await _redis.KeyExpireAsync(monthKey, TimeSpan.FromDays(180));
    }
}
```

## CI/CD Integration

Trigger provisioning from your deployment pipeline:

```yaml
# GitHub Actions — provision-tenant job
jobs:
  provision-tenant:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Provision tenant
        run: |
          dotnet run --project src/YourApp.ProvisioningTool/ \
            --tenant-id "${{ inputs.tenant_id }}" \
            --connection-string "${{ secrets.SHARED_DB }}"
```

## Tenant Deprovisioning

```csharp
public async Task DeprovisionAsync(string tenantId, CancellationToken ct)
{
    // 1. Notify tenant admin
    // 2. Backup tenant database
    // 3. Archive cost records
    // 4. Drop tenant database
    // 5. Remove from TenantConfigurationStore
    // 6. Evict chat client cache
    // 7. Remove Redis cost counters
}
```

## Per-Tenant Plan Defaults

```csharp
public static TenantInfo CreateFromPlan(string identifier, string name, Plan plan)
{
    var tenant = new TenantInfo
    {
        Identifier = identifier,
        Name = name,
        ProviderType = plan.ProviderType,
        Model = plan.Model,
    };

    (tenant.DailyBudgetUsd, tenant.MonthlyBudgetUsd, tenant.MaxTurnsPerSession) = plan switch
    {
        Plan.Free       => (0.50m, 10.00m, 50),
        Plan.Pro        => (5.00m, 100.00m, 200),
        Plan.Enterprise => (100.00m, 5000.00m, 1000),
        _               => (0m, 0m, 50)
    };

    return tenant;
}
```
