# Custom SessionId Approach — Plain English Guide

> Part of the [ab-chat-session-management](SKILL.md) skill. For the full session ID resolution chain, see [session-id-resolution.md](session-id-resolution.md).

## TL;DR

Yes, you can generate your own `Guid` (or any string) in your chat UI component and pass it as the `SessionId` parameter to `AgentChatSurface`. This is a **legitimate and supported approach** called "consumer-provided session ID" (Path B in the documentation).

## How It Works

### The Simple Version

1. **Your component generates a Guid**: `var sessionId = Guid.NewGuid().ToString();`
2. **You pass it to the chat surface**: `<AgentChatSurface SessionId="@sessionId" />`
3. **AgentBlazor uses your Guid**: Instead of the circuit's auto-generated ID, your Guid becomes the session identifier.
4. **Everything else works the same**: Conversation history, agent isolation, persistence—all work with your custom ID.

### What Happens Under the Hood

When you provide a `SessionId` parameter:

```
Your component generates: "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
                              ↓
AgentChatSurface receives it as the SessionId parameter
                              ↓
EffectiveSessionId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"  (your Guid)
                              ↓
If you have multiple agents with isolation ON:
  SessionId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890::agent::SupportAgent"
                              ↓
Database stores:
  BaseSessionId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"  (your Guid)
  SessionId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890::agent::SupportAgent"
  AgentName = "SupportAgent"
```

## When You Might Want This

### 1. **Session Persistence Across Page Navigation**
If your chat needs to survive navigating away and coming back, a custom session ID lets you:
- Store the ID in browser storage (localStorage/sessionStorage)
- Resume the same conversation later

### 2. **Business-Driven Session IDs**
Use meaningful IDs instead of opaque hex:
- `ticket-42` for support tickets
- `user-123-conversation-456` for user conversations
- `order-789-support` for order-specific support

### 3. **Multi-Tab Scenarios**
Each browser tab gets its own unique session ID automatically, but you have full control over the format.

### 4. **Integration with External Systems**
Use IDs from your own database or authentication system, making it easier to correlate conversations with business data.

### 5. **Clean Separation of Concerns**
Keep session identity separate from tenant isolation:
- Session ID identifies the conversation
- Tenant ID (in entity model) handles multi-tenant isolation
- No redundant data embedding

## Important Considerations

### 1. **Tenant Isolation: Use Entity Model, NOT Session Key Embedding**

**DO NOT** embed tenant IDs in session keys (e.g., `"tenant-123:a1b2c3d4..."`). This is an outdated pattern that creates redundant data and complicates queries.

**DO** rely on the `TenantId` column in the entity model for tenant isolation:

```csharp
// ✅ CORRECT: Tenant isolation via entity model
public class ConversationSessionEntity
{
    public string SessionId { get; set; } = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
    public string TenantId { get; set; } = "tenant-123";  // ← Tenant isolation here
    // ...
}

// ❌ WRONG: Don't embed tenant ID in session key
// SessionId = "tenant-123:a1b2c3d4-e5f6-7890-abcd-ef1234567890"
```

**Why?**
- The `TenantId` column is the authoritative tenant identifier
- Embedding tenant IDs in session keys is redundant and creates maintenance burden
- Queries filter by `TenantId` column, not by parsing session keys
- The `TenantContextAccessor` (Finbuckle) handles tenant resolution automatically

### 2. **Stability Within a Component Lifecycle**

**DO**: Generate the Guid **once** when the component initializes:

```razor
@code {
    // Generate once when the component initializes
    private string _sessionId = Guid.NewGuid().ToString();
    
    // This stays the same for the lifetime of this component instance
}
```

**DON'T**: Regenerate on every render—this creates a new session each time:

```razor
@code {
    // BAD: This regenerates on every render!
    private string _sessionId => Guid.NewGuid().ToString();
}
```

### 2. **Session Continuity Across Page Refreshes**

If you want the same session to resume after a page refresh, you need to persist the session ID:

```razor
@inject ILocalStorageService LocalStorage

@code {
    private string _sessionId = string.Empty;
    
    protected override async Task OnInitializedAsync()
    {
        // Try to restore from storage
        _sessionId = await LocalStorage.GetItemAsync<string>("chat-session-id") 
                     ?? Guid.NewGuid().ToString();
        
        // Save for next time
        await LocalStorage.SetItemAsync("chat-session-id", _sessionId);
    }
}
```

### 3. **Entity Model Implications**

Your custom session ID affects the entity model:

| Column | Value with Your Custom Guid |
|--------|----------------------------|
| `BaseSessionId` | Your Guid (e.g., `"a1b2c3d4-e5f6-7890-abcd-ef1234567890"`) |
| `SessionId` | Your Guid + agent suffix (if isolation ON with multiple agents) |
| `AgentName` | Populated only if you have multiple agents with isolation enabled |

### 4. **Query Patterns**

When querying the database:

```sql
-- Find all conversations for your component
SELECT * FROM ConversationSessions 
WHERE BaseSessionId = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';

-- Find a specific agent's conversation
SELECT * FROM ConversationSessions 
WHERE SessionId = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890::agent::SupportAgent';
```

## Comparison with Default Behavior

| Aspect | Default (Circuit ID) | Your Approach (Custom Guid) |
|--------|---------------------|----------------------------|
| **Survives page refresh** | No (new circuit = new ID) | Only if you persist the Guid |
| **Unique per browser tab** | Yes | Yes |
| **Business-meaningful** | No (opaque hex) | Yes (you control the format) |
| **Multi-tab isolation** | Automatic | Manual (each tab gets new Guid) |
| **Control over format** | None (32-char hex) | Full control |
| **Integration with external systems** | Hard (opaque ID) | Easy (meaningful ID) |
| **Tenant isolation** | Via TenantId column (recommended) | Via TenantId column (recommended) |
| **Session key embedding** | Not recommended | Not recommended |

## Code Examples

### Example 1: Simple Custom Session ID

```razor
<AgentChatSurface SessionId="@_sessionId" />

@code {
    private string _sessionId = Guid.NewGuid().ToString();
}
```

### Example 2: Business-Driven Session ID

```razor
<AgentChatSurface SessionId="@($"ticket-{TicketId}")" />

@code {
    [Parameter]
    public int TicketId { get; set; }
}
```

### Example 3: Session Persistence with LocalStorage

```razor
@inject ILocalStorageService LocalStorage

<AgentChatSurface SessionId="@_sessionId" />

@code {
    private string _sessionId = string.Empty;
    
    protected override async Task OnInitializedAsync()
    {
        var storageKey = $"chat-session-{TicketId}";
        _sessionId = await LocalStorage.GetItemAsync<string>(storageKey) 
                     ?? Guid.NewGuid().ToString();
        
        await LocalStorage.SetItemAsync(storageKey, _sessionId);
    }
}
```

### Example 4: User-Specific Session

```razor
@inject AuthenticationStateProvider Auth

<AgentChatSurface SessionId="@_sessionId" />

@code {
    private string _sessionId = "global";
    
    protected override async Task OnInitializedAsync()
    {
        var auth = await Auth.GetAuthenticationStateAsync();
        var user = auth.User;
        
        if (user.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _sessionId = $"user-{userId}-support";
        }
    }
}
```

## Bottom Line

**Yes, your approach is perfectly legitimate.** AgentBlazor explicitly supports consumer-provided session IDs. The key is to:

1. Generate the Guid **once** per component lifecycle
2. Decide whether you need to persist it across page refreshes
3. Understand that this becomes the `BaseSessionId` in the entity model

This gives you full control over session identity while still using all of AgentBlazor's conversation persistence and agent isolation features.

## See Also

- [Session ID Resolution](session-id-resolution.md) — Full fallback chain and initialization patterns
- [Session Lifecycle](session-lifecycle.md) — 5-phase lifecycle with per-store categorization
- [Frontend Hydration](frontend-hydration.md) — Building session browser UIs
- [Entity Design](../../ab-entity-design/SKILL.md) — Entity model implications of session identity
- [Conversation Store](../../ab-conversation-store/SKILL.md) — Persistence options and configuration