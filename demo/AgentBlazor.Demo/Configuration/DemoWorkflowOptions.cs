namespace AgentBlazor.Demo.Configuration;

/// <summary>
/// Demo workflow-database configuration.
/// <para>
/// The Demo uses the unified SQL Server database (<c>demo-db</c>) to persist
/// business-process state (dojo recipes, incidents, supplier compliance, etc.)
/// that agents operate on. The connection string is injected by the Aspire AppHost.
/// </para>
/// </summary>
internal sealed class DemoWorkflowOptions
{
    public const string SectionName = "DemoWorkflow";
}
