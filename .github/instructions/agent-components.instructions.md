---
description: "Agent component patterns for AgentBlazor. Use when creating new Blazor components that are agent-controllable. Covers component base classes, attribute usage, and rendering conventions."
applyTo: "src/AgentBlazor.Components/**/*.razor"
---

# Agent Component Patterns

## Overview

AgentBlazor provides base classes and attributes for creating Blazor components that can be controlled by agents.

## Base Classes

1. **`AgentControllableComponentBase`** — Base class for agent-controllable components
2. **`AgentFormPageBase<TModel>`** — Base class for form pages with agent support

## Conventions

1. **Inheritance**: Inherit from `AgentControllableComponentBase` for agent-controllable components
2. **Attributes**: Use `[AgentComponent]` to mark components as agent-controllable
3. **Properties**: Use `[AgentReadable]` for properties agents can read
4. **Actions**: Use `[AgentAction]` for methods agents can invoke
5. **Parameters**: Use `[AgentParam]` for method parameters

## Best Practices

1. **CSS isolation**: Use `.razor.css` files for component styling
2. **Static assets**: Place in `wwwroot/` with `_content/AgentBlazor/` prefix
3. **Namespace**: Follow `AgentBlazor.Components.*` namespace conventions
4. **Internal visibility**: Use `internal` for component implementation details

## Example

```razor
@inherits AgentControllableComponentBase

<MudCard>
    <MudCardContent>
        <MudText>@Title</MudText>
        <MudText Typo="Typo.Body2">@Description</MudText>
    </MudCardContent>
</MudCard>

@code {
    [Parameter]
    [AgentReadable]
    public string Title { get; set; } = string.Empty;

    [Parameter]
    [AgentReadable]
    public string Description { get; set; } = string.Empty;

    [AgentAction("Update the title")]
    public void UpdateTitle(string newTitle)
    {
        Title = newTitle;
        StateHasChanged();
    }
}
```

## References

- See `src/AgentBlazor.Components/Wrappers/AgentControllableComponentBase.cs` for base class
- See `src/AgentBlazor.Components/Base/AgentFormPageBase.cs` for form base class
- See `.github/skills/ab-mud-components/` for MudBlazor wrapper patterns