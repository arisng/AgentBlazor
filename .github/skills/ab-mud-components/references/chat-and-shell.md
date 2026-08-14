# Shell, Providers & Chat Components Reference

## Shell and Provider Hierarchy

The correct nesting order (top to bottom):

```
AgentThemeProvider         — cascades ChatTheme and CSS variables
  └─ AgentPopoverProvider  — cascades popover/overlay support
      └─ AgentDialogProvider — cascades dialog/modal support
          └─ AgentSnackbarProvider — cascades snackbar/toast support
              └─ [your content]
              └─ AgentChatWidget (auto-included by AgentBlazorShell)
```

### AgentBlazorShell

Convenience wrapper that nests all five providers and includes `AgentChatWidget` automatically.

```razor
<AgentBlazorShell>
    @Body
</AgentBlazorShell>
```

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `ChildContent` | `RenderFragment?` | Page content |

### Individual Providers

Each provider cascades itself via `<CascadingValue Value="this" IsFixed="true">`. All four providers have identical API:

```razor
<AgentThemeProvider>
    <AgentPopoverProvider>
        <AgentDialogProvider>
            <AgentSnackbarProvider>
                @ChildContent
            </AgentSnackbarProvider>
        </AgentDialogProvider>
    </AgentPopoverProvider>
</AgentThemeProvider>
```

**Parameters:** All take a single `ChildContent` parameter (`RenderFragment?`).

---

## Chat Components

### AgentChatSurface

The core chat component — timeline, input, agent state indicators. Embed in pages or use via `AgentChatWidget` / `AgentChatPanel`.

**Key Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Title` | string? | null | Chat header title |
| `Description` | string? | null | Subtitle rendered below the title |
| `DefaultAgentName` | string? | null | Agent selected on load |
| `ShowAgentSelector` | bool | true | Show agent dropdown |
| `LockedAgentName` | string? | null | Lock to one agent (hides selector) |
| `LockAgentToCurrentRoute` | bool | false | Auto-switch agents on route change |
| `EnableAgentHandoff` | bool | true | Allow agent-to-agent handoff |
| `SessionId` | string? | null | Explicit session key |
| `Placeholder` | string | "Type a message…" | Input placeholder text |
| `EnableGeneratedUi` | bool | true | Enable agent-generated UI blocks inline |
| `ShowExecutionDetails` | bool | false | Show planning steps and activity log |
| `ShowDevTools` | bool | false | Show dev tools toggle |
| `AutoShowDevTools` | bool | false | Auto-open dev tools on error |
| `FormName` | string? | null | Associates the chat with an `<EditForm>` for SSR |
| `Theme` | ChatTheme? | null | Theme configuration |
| `CssClass` | string? | null | Additional CSS class |
| `RequireHandoffApproval` | bool | false | Require user approval for handoffs |
| `HandoffPolicy` | `IReadOnlyDictionary<string, IReadOnlyList<string>>?` | null | Restrict handoff flow (key: source agent, value: allowed targets) |
| `HandoffApprovalPolicy` | `IReadOnlyDictionary<string, IReadOnlyList<string>>?` | null | Per-agent approval requirements |
| `MaxHandoffsPerSession` | int? | null | Limit handoffs per session |
| `MaxHandoffsPerPair` | int? | null | Limit handoffs between two agents |
| `MaxHandoffsPerWindow` | int? | null | Limit handoffs within a time window |
| `HandoffWindowMinutes` | int? | null | Time window for handoff counting |
| `MaxPairHandoffsPerWindow` | int? | null | Limit handoffs per agent pair in window |
| `BlockImmediateReturnHandoff` | bool | false | Prevent bounce-back handoff |

**Agent State Indicators:** The surface shows:
- **Thinking...** — agent is processing
- **Taking longer than expected...** — timeout warning during thinking
- **Waiting for approval** — agent hit a `RequiresApproval` action
- **Clarification needed** — agent needs user input

**Injected Services:** `IAgentRegistry`, `IAgentRuntimeAdapter`, `IAgentDeferredActionEvents`, `IConversationStore`, `IAgentComponentRegistry`, `IAgentChatSessionEvents`, `IJSRuntime`, `NavigationManager`, `IAgentActionRenderRegistry`, `IAdaptiveSuggestionService`, `IProactiveInsightService`, `IOptions<AgentBlazorOptions>`, `IServiceProvider`, `IAgentExecutionScopeAccessor`

---

### AgentChatWidget

Floating bottom-right chat bubble with popup window. Wraps `AgentChatSurface`.

```razor
<AgentChatWidget Title="Support" />
```

**Key Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Title` | string? | "AgentBlazor Chat Widget" | Widget header |
| `Description` | string? | null | Subtitle |
| `BubbleLabel` | string | "Ask Agent" | Label on the floating bubble |
| `BubbleAriaLabel` | string? | null | Accessibility label |
| `Width` | string | "360px" | Widget window width |
| `Height` | string | "560px" | Widget window height |
| `Right` | string | "1.25rem" | Distance from right edge |
| `Bottom` | string | "1.25rem" | Distance from bottom edge |
| `SessionId` | string? | null | Explicit session key |
| *(Plus all AgentChatSurface handoff/approval params)* | | | |

All `AgentChatSurface` parameters are forwarded through (Title, Description, DefaultAgentName, ShowAgentSelector, LockedAgentName, EnableAgentHandoff, SessionId, Placeholder, EnableGeneratedUi, ShowExecutionDetails, Theme, CssClass, handoff/approval params).

**Injected Service:** `IAgentChatWidgetState` — controls open/close state.

---

### AgentChatPanel

Docked side-panel chat. Wraps `AgentChatSurface`.

```razor
<AgentChatPanel Width="350px" Title="Assistant" />
```

**Key Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Width` | string | "350px" | Panel width |
| `Height` | string | "100%" | Panel height |
| `Title` | string? | "Agent Assistant" | Panel header |
| *(Plus all AgentChatSurface params)* | | | |

All `AgentChatSurface` parameters are forwarded through identically.

---

### AgentChatBar

Inline chat input bar with suggestion chips and optional inline timeline.

```razor
<AgentChatBar Placeholder="Ask me anything..."
              Suggestions="@("Hello", "Help")"
              ShowTimeline="true" />
```

**Key Parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Placeholder` | string? | null | Input placeholder |
| `Icon` | string | Icons.Material.Filled.SmartToy | Input icon |
| `ShowIcon` | bool | true | Show input icon |
| `Suggestions` | `IReadOnlyList<string>?` | null | Quick-action suggestion chips |
| `ShowTimeline` | bool | false | Show inline conversation history |
| `ShowExecutionDetails` | bool | false | Show planning steps |
| `DefaultAgentName` | string? | null | Default agent |
| `SessionId` | string? | null | Explicit session key |
| `FormName` | string? | null | SSR form association |
| `Theme` | ChatTheme? | null | Theme configuration |
| `CssClass` | string? | null | Additional CSS class |

**Injected Services:** `IAgentRuntimeAdapter`, `IAgentDeferredActionEvents`, `IConversationStore`, `IAgentChatSessionState`, `IAgentChatSessionEvents`, `IAgentActionRenderRegistry`, `NavigationManager`, `IOptions<AgentBlazorOptions>`, `IServiceProvider`, `IAgentExecutionScopeAccessor`, `IJSRuntime`

---

## ChatTheme Configuration

Full theming for all chat components. Apply via `Theme` parameter on any chat component.

### Built-in Variants

| Variant | Description |
|---|---|
| `ChatThemeVariant.Default` | Host-controlled (no override) |
| `ChatThemeVariant.Light` | Light color scheme |
| `ChatThemeVariant.Dark` | Dark color scheme |
| `ChatThemeVariant.System` | Follows system preference |

### Visual Styles

| Style | Description |
|---|---|
| `ChatVisualStyle.Modern` | Clean, flat design |
| `ChatVisualStyle.Classic` | Traditional bubble style |

### Border Radius

`None`, `Small`, `Medium`, `Large`, `Full`

### Spacing

`Compact`, `Normal`, `Comfortable`

### Color Overrides

`AccentColor`, `BackgroundColor`, `UserBubbleColor`, `AssistantBubbleColor`, `TextColor`, `TextMutedColor`, `BorderColor`

### Typography

`FontFamily`, `FontSize`

### Visual Effects

`EnableGlassEffect` (default: true), `EnableAnimations` (default: true), `EnableShadows` (default: true)

### Component Visibility

`ShowTimestamps`, `ShowTypingIndicator`, `ShowMessageStatus`, `ShowAvatars`, `UserAvatar`, `AssistantAvatar`

### Layout

`MessageAlignment` (Aligned / Left / Right), `InputPosition` (Bottom / Top), `MaxBubbleWidth`

---

## Action Rendering

### AgentActionRender

Registers custom `RenderFragment` delegates for each action state (in-progress, executing, complete, failed). Pure side-effect — no visible output.

```razor
<AgentActionRender AgentId="AgentForm"
                   ActionId="submit"
                   InProgress="@(ctx => <span>Working...</span>)"
                   Complete="@(ctx => <MudIcon Icon="@Icons.Material.Filled.Check" />)"
                   Failed="@(ctx => <span>@ctx.ErrorMessage</span>)" />
```

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `AgentId` | string (required) | Component identifier matching `AgentId` on the target |
| `ActionId` | string (required) | Action identifier (e.g. "submit", "filter") |
| `InProgress` | `RenderFragment<ActionRenderContext>?` | Shown when action is queued |
| `Executing` | `RenderFragment<ActionRenderContext>?` | Shown while action is executing |
| `Complete` | `RenderFragment<ActionRenderContext>?` | Shown after success |
| `Failed` | `RenderFragment<ActionRenderContext>?` | Shown after failure (falls back to Complete if null) |

### AgentToolRender

Convenience wrapper over `AgentActionRender`. Accepts either `ToolId` (`"ComponentId.ActionId"` format) or separate `ComponentId` + `ActionId`.

```razor
<AgentToolRender ToolId="AgentForm.submit" Complete="@(ctx => ...)" />
<!-- Equivalent: -->
<AgentToolRender ComponentId="AgentForm" ActionId="submit" Complete="@(ctx => ...)" />
```

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `ToolId` | string? | Full tool id in `ComponentId.ActionId` format |
| `ComponentId` | string? | Runtime component id |
| `ActionId` | string? | Action id for the component |
| *(Plus same InProgress/Executing/Complete/Failed)* | | |

### IAgentActionRenderRegistry

Service interface for registering/unregistering action render fragments. Default implementation: `InMemoryAgentActionRenderRegistry` (registered as scoped).

```csharp
public interface IAgentActionRenderRegistry
{
    void Register(string agentId, string actionId, ActionRenderFragments fragments);
    void Unregister(string agentId, string actionId);
    ActionRenderFragments? TryGet(string agentId, string actionId);
}
```
