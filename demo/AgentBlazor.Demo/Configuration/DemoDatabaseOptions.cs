namespace AgentBlazor.Demo.Configuration;

/// <summary>
/// Configuration for the unified Demo SQL Server database backing
/// <see cref="AgentBlazor.Demo.Data.DemoDbContext"/> — conversation sessions/turns
/// and agent definitions in a single database managed by code-first migrations.
/// </summary>
/// <remarks>
/// The connection string is injected by the Aspire AppHost as <c>ConnectionStrings:demo-db</c>.
/// For standalone development, add it to <c>appsettings.json</c>.
/// </remarks>
internal sealed class DemoDatabaseOptions
{
    public const string SectionName = "DemoDatabase";
}
