namespace AgentBlazor.Demo.Configuration;

/// <summary>
/// Configuration for the unified Demo SQLite database backing
/// <see cref="AgentBlazor.Demo.Data.DemoDbContext"/> — conversation sessions/turns
/// and agent definitions in a single database managed by code-first migrations.
/// </summary>
internal sealed class DemoDatabaseOptions
{
    public const string SectionName = "DemoDatabase";

    /// <summary>
    /// SQLite connection string for the unified Demo database.
    /// When empty, resolved at startup to <c>{ContentRootPath}/data/agentblazor-demo.db</c>.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
