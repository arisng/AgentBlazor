namespace AgentBlazor.Demo.Configuration;

/// <summary>
/// Demo workflow-database configuration.
/// <para>
/// The Demo uses a dedicated SQLite database to persist business-process state
/// (dojo recipes, incidents, supplier compliance, etc.) that agents operate on.
/// The connection string follows the same convention as
/// <see cref="DemoDatabaseOptions.ConnectionString"/>: when empty, it resolves
/// at startup to <c>{ContentRootPath}/data/agentblazor-demo-workflow.db</c>.
/// </para>
/// </summary>
internal sealed class DemoWorkflowOptions
{
    public const string SectionName = "DemoWorkflow";

    /// <summary>
    /// SQLite connection string for the workflow database.
    /// When empty, resolved at startup to
    /// <c>{ContentRootPath}/data/agentblazor-demo-workflow.db</c>.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
