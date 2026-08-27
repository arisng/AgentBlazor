namespace AgentBlazor.Demo.Data;

/// <summary>
/// EF Core entity for a conversation session (one per agent chat session).
/// Follows the canonical entity shape from
/// <c>.github/skills/ab-entity-design</c> — session metadata (UserId, creation, last
/// activity) is set once and must never be rewritten by incremental turn operations.
/// </summary>
internal sealed class DemoConversationSessionEntity
{
    public int Id { get; set; }

    public required string SessionId { get; set; }

    public string? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;

    public List<DemoConversationTurnEntity> Turns { get; set; } = [];
}