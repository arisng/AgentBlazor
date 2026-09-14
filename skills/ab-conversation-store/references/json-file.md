# JsonFileConversationStore

File-backed conversation store that persists all sessions as a single JSON file on disk. Survives process restarts.

## Location

`src/AgentBlazor.Core/Runtime/Conversation/JsonFileConversationStore.cs`

Internal class — instantiated only via the `UseJsonFileConversationStore` builder helper.

## How to register

```csharp
builder.UseJsonFileConversationStore(
    filePath: "data/conversations.json",
    configure: options =>
    {
        options.PersistAcrossRestarts = true;
        options.MaxTurnsPerSession = 100;
    });
```

The builder method configures `PersistAcrossRestarts = true` automatically.

## File format

Single JSON file containing a serialized `ConversationStoreSnapshot`:

```json
{
  "sessions": {
    "abc123": {
      "sessionId": "abc123",
      "turns": [
        {
          "timestamp": "2026-07-15T10:00:00Z",
          "userMessage": "Hello",
          "agentResponse": "Hi! How can I help?",
          "plannedActions": [],
          "executionResults": [],
          "executionPlan": null,
          "generatedUi": null
        }
      ],
      "createdAt": "2026-07-15T10:00:00Z",
      "lastActivityAt": "2026-07-15T10:00:05Z",
      "userId": null
    }
  }
}
```

## Load / Save mechanics

**Load** — constructor calls `LoadSnapshot()`:

1. Creates directory if missing.
2. If file doesn't exist, start empty.
3. Deserializes JSON, skips expired or null sessions.
4. Rebuilds user-index from loaded sessions.
5. Evicts sessions exceeding `MaxSessions`.

**Save** — every mutation calls `PersistSnapshotAsync()`:

1. Acquires `SemaphoreSlim(1,1)` write lock.
2. Serializes all sessions to a `.tmp` file (`conversations.json.tmp`).
3. Atomically moves `.tmp` over the original file via `File.Move(..., overwrite: true)`.
4. Atomic move prevents corruption — at worst the previous snapshot survives a crash.

## Thread safety

Same as in-memory plus `SemaphoreSlim(1,1)` for file writes. User-index uses the same lock-guarded `HashSet` pattern.

## When to use

- Need conversation history to survive app restarts
- Single-process deployment
- No desire or ability to set up a database
- Acceptable to lose at most one write on crash (atomic move minimizes this)
- Fewer than ~1,000 sessions for reasonable file size and load time
