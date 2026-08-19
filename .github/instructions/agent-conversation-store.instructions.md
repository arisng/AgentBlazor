---
description: "Conversation store patterns for AgentBlazor. Use when implementing conversation persistence, choosing storage providers, or debugging conversation history. Covers IConversationStore implementations and usage."
applyTo: "src/AgentBlazor.Core/Runtime/Conversation/**/*.cs"
---

# Conversation Store Patterns

## Overview

AgentBlazor provides conversation persistence through the `IConversationStore` interface with multiple implementations.

## Implementations

1. **InMemoryConversationStore** — In-memory with automatic TTL cleanup
2. **JsonFileConversationStore** — File-backed JSON for basic persistence
3. **SQLite implementations** — (Paid tier) SQLite-based persistence

## Interface

```csharp
public interface IConversationStore
{
    Task AppendTurnAsync(string sessionId, ConversationTurn turn);
    Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId);
    Task ClearSessionAsync(string sessionId);
    Task<IReadOnlyList<string>> GetActiveSessionsAsync();
    Task SetUserIdAsync(string sessionId, string userId);
    Task<IReadOnlyList<string>> GetSessionsForUserAsync(string userId);
}
```

## Conventions

1. **Registration**: Use `UseConversationStore<TStore>()` to register custom stores
2. **Factory**: Use `UseConversationStore(Func<IServiceProvider, IConversationStore>)` for factory pattern
3. **Default**: `InMemoryConversationStore` is the default implementation
4. **Internal**: All built-in implementations are internal

## Best Practices

1. **Session management**: Use session IDs for conversation isolation
2. **User association**: Use `SetUserIdAsync` for user-scoped sessions
3. **Cleanup**: Implement `IDisposable` for resource cleanup
4. **TTL**: Use time-to-live for automatic cleanup of old sessions

## Usage

```csharp
// Register custom store
builder.Services.AddAgentBlazor(options =>
{
    options.UseConversationStore<MyCustomStore>();
});

// Or use factory
builder.Services.AddAgentBlazor(options =>
{
    options.UseConversationStore(sp => 
        new MyCustomStore(sp.GetRequiredService<IDbContextFactory<MyDbContext>>()));
});
```

## References

- See `src/AgentBlazor.Core/Runtime/Interfaces/IConversationStore.cs` for interface
- See `src/AgentBlazor.Core/Runtime/Conversation/` for implementations
- See `.github/skills/ab-conversation-store/` for detailed guidance