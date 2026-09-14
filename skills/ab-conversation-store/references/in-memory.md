# InMemoryConversationStore

Default conversation store in AgentBlazor. Ephemeral — all sessions are lost on process restart.

## Location

`src/AgentBlazor.Core/Runtime/Conversation/InMemoryConversationStore.cs`

Registered automatically as a singleton in `AddAgentBlazor()`:

```csharp
services.TryAddSingleton<IConversationStore, InMemoryConversationStore>();
```

## Internal state

- `ConcurrentDictionary<string, ConversationHistory> _sessions` — keyed by session ID (case-insensitive).
- `ConcurrentDictionary<string, HashSet<string>> _userSessions` — maps user ID → set of session IDs.

## Cleanup

- A `Timer` runs every `CleanupInterval` (default 1 hour) and removes sessions whose `LastActivityAt` exceeds `SessionTimeout` (default 24h).
- When `_sessions.Count > MaxSessions` (default 10,000), the oldest 10% of sessions by `LastActivityAt` are evicted.
- `GetHistoryAsync` checks expiry on read — expired sessions are removed and return `null`.

## Turn trimming

When `AppendTurnAsync` would cause `Turns.Count > MaxTurnsPerSession` (default 50), the oldest turns are dropped:

```csharp
updated = updated with
{
    Turns = updated.Turns
        .Skip(updated.Turns.Count - _options.MaxTurnsPerSession)
        .ToArray()
};
```

## Thread safety

- Uses `ConcurrentDictionary` for session/user-index maps.
- `SemaphoreSlim(1,1)` guards the cleanup loop.
- User-index mutations are lock-guarded per `HashSet`.

## When this is enough

- Single-process demo apps
- Development and testing
- Acceptable to lose conversation history on restart
- Fewer than ~10,000 concurrent sessions
