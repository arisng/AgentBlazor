using AgentBlazor.Demo.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// Design-time factory for <see cref="DemoDbContext"/> so EF Core CLI tools
/// (<c>dotnet ef migrations add</c>) can scaffold migrations outside the runtime host.
/// Reads the connection string from <c>appsettings.json</c> → <c>DemoDatabase:ConnectionString</c>,
/// falling back to a local file when not configured.
/// </summary>
public sealed class DemoDbContextFactory : IDesignTimeDbContextFactory<DemoDbContext>
{
    public DemoDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration
            .GetSection(DemoDatabaseOptions.SectionName)
            .Get<DemoDatabaseOptions>()?.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = $"Data Source={Path.Combine("data", "agentblazor-demo.db")}";
        }

        var optionsBuilder = new DbContextOptionsBuilder<DemoDbContext>();
        optionsBuilder.UseSqlite(connectionString);
        return new DemoDbContext(optionsBuilder.Options);
    }
}
