namespace AgentBlazor.Demo.Configuration;

/// <summary>
/// Demo conversation-store configuration.
/// <para>
/// The Demo demonstrates AgentBlazor's incremental conversation persistence model:
/// turns are appended once per turn and edits are applied as targeted patches
/// (<see cref="AgentBlazor.Core.Runtime.Interfaces.IConversationStore.UpdateTurnAsync"/>),
/// never as a full-history clear-and-rebuild. Three store backends are selectable:
/// </para>
/// <list type="bullet">
/// <item><c>JsonFile</c> (default) — durable, survives process restarts; conversation
/// history is written to <see cref="FilePath"/>.</item>
/// <item><c>EFCore</c> — durable SQLite-backed store through
/// <see cref="AgentBlazor.Demo.Data.DemoConversationDbContext"/>, demonstrating a custom
/// EF Core <c>IConversationStore</c> implementation (the production-database pattern).</item>
/// <item><c>InMemory</c> — ephemeral, reset on process restart (classic demo default).</item>
/// </list>
/// </summary>
internal sealed class DemoConversationOptions
{
    public const string SectionName = "DemoConversation";

    /// <summary>
    /// Store backend name: "JsonFile" (default), "EFCore", or "InMemory".
    /// Values are matched case-insensitively.
    /// </summary>
    public string Store { get; set; } = "JsonFile";

    /// <summary>
    /// Absolute or content-root-relative JSON file path used when <see cref="Store"/>
    /// is <c>JsonFile</c>. Defaults to a temp file so the Demo never writes into the
    /// repo tree or a read-only deployment volume.
    /// </summary>
    public string FilePath { get; set; } = Path.Combine(Path.GetTempPath(), "agentblazor-demo-conversations.json");

    /// <summary>
    /// SQLite connection string used when <see cref="Store"/> is <c>EFCore</c>.
    /// Defaults to a temp database file so the Demo never writes into the repo tree.
    /// </summary>
    public string ConnectionString { get; set; } =
        $"Data Source={Path.Combine(Path.GetTempPath(), "agentblazor-demo-conversations.db")}";

    /// <summary>
    /// Maximum turns kept per conversation session. Older turns are trimmed.
    /// </summary>
    public int MaxTurnsPerSession { get; set; } = 100;

    /// <summary>
    /// Inactive-session retention before automatic cleanup.
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromHours(24);
}