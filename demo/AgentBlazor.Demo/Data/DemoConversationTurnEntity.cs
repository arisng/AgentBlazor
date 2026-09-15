using AgentBlazor.Core.Persistence;

namespace AgentBlazor.Demo.Data;

/// <summary>
/// Demo-specific turn entity — all core properties (including token cost columns)
/// are inherited from <see cref="ConversationTurnEntity"/>.
/// </summary>
public sealed class DemoConversationTurnEntity : ConversationTurnEntity
{
}