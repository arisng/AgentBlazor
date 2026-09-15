using AgentBlazor.Core.Persistence;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// Demo-specific session entity — all core properties are inherited from
/// <see cref="ConversationSessionEntity"/>. Uses the base <c>Turns</c> navigation
/// directly (do NOT shadow with <c>new</c> — that creates a separate backing field
/// which breaks EF Core Include under TPC mapping).
/// </summary>
public sealed class DemoConversationSessionEntity : ConversationSessionEntity
{
}