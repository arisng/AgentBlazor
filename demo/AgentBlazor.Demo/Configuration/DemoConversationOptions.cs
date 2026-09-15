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
/// <item><c>EFCore</c> — durable SQLite-backed store through the unified
/// <see cref="AgentBlazor.Demo.Data.DemoDbContext"/>, demonstrating a custom
/// EF Core <c>IConversationStore</c> implementation (the production-database pattern).
/// The connection string is configured via <c>DemoDatabase:ConnectionString</c>.</item>
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
    /// is <c>JsonFile</c>. When empty, resolved at startup to
    /// <c>{ContentRootPath}/data/agentblazor-demo-conversations.json</c>.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Maximum turns kept per conversation session. Older turns are trimmed.
    /// </summary>
    public int MaxTurnsPerSession { get; set; } = 100;

    /// <summary>
    /// Inactive-session retention before automatic cleanup.
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromHours(24);
}