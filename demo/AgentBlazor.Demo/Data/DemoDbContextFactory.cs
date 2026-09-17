using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// Design-time factory for <see cref="DemoDbContext"/> so EF Core CLI tools
/// (<c>dotnet ef migrations add</c>) can scaffold migrations outside the runtime host.
/// Internal because EF Core discovers factories via reflection, and this allows sharing
/// the internal <see cref="DesignTimeConnectionString"/> helper.
/// Reads the connection string from <c>appsettings.json</c> → <c>ConnectionStrings:demo-db</c>.
/// </summary>
internal sealed class DemoDbContextFactory : IDesignTimeDbContextFactory<DemoDbContext>
{
    public DemoDbContext CreateDbContext(string[] args)
    {
        var connectionString = DesignTimeConnectionString.GetConnectionString();
        var optionsBuilder = new DbContextOptionsBuilder<DemoDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new DemoDbContext(optionsBuilder.Options);
    }
}

/// <summary>
/// Design-time factory for <see cref="DemoWorkflowDbContext"/> so EF Core CLI tools
/// can scaffold migrations outside the runtime host. Shares the same <c>demo-db</c> database.
/// </summary>
internal sealed class DemoWorkflowDbContextFactory : IDesignTimeDbContextFactory<DemoWorkflowDbContext>
{
    public DemoWorkflowDbContext CreateDbContext(string[] args)
    {
        var connectionString = DesignTimeConnectionString.GetConnectionString();
        var optionsBuilder = new DbContextOptionsBuilder<DemoWorkflowDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new DemoWorkflowDbContext(optionsBuilder.Options);
    }
}

internal static class DesignTimeConnectionString
{
    internal static string GetConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        return configuration.GetConnectionString("demo-db")
            ?? throw new InvalidOperationException(
                "Connection string 'demo-db' not found in appsettings.json. " +
                "When running via Aspire AppHost, this is injected automatically. " +
                "For design-time tools, add 'ConnectionStrings:demo-db' to appsettings.json.");
    }
}
