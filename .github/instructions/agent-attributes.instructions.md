---
description: "Agent attribute patterns for AgentBlazor. Use when creating new agent capabilities, actions, or components. Covers attribute usage, conventions, and best practices."
applyTo: "src/AgentBlazor.Core/Attributes/**/*.cs"
---

# Agent Attribute Patterns

## Overview

AgentBlazor uses attributes to drive capability discovery and agent-UI interaction. Key attributes:

- `[AgentCapability]` — Marks a class as an agent capability
- `[AgentAction]` — Marks a method as an agent action
- `[AgentReadable]` — Marks a property/field as readable by agents
- `[AgentParam]` — Marks a parameter as an agent parameter
- `[AgentComponent]` — Marks a Blazor component as agent-controllable

## Conventions

1. **Naming**: Use PascalCase for attribute classes (e.g., `AgentActionAttribute`)
2. **Namespace**: Place in `AgentBlazor.Core.Attributes` namespace
3. **Visibility**: Keep attributes internal unless needed externally
4. **Documentation**: Add XML documentation for public attributes

## Best Practices

1. **Description**: Always provide meaningful descriptions for actions
2. **Parameters**: Use `[AgentParam]` for action parameters
3. **Context binding**: Use `ContextKey` on `[AgentParam]` to bind from runtime context instead of expecting the AI to supply the value — use for internal identifiers (session ID, user ID, run ID) that the model should never fabricate
4. **Return types**: Return `CapabilityResult` from agent actions
5. **Approval**: Set `RequiresApproval = true` for sensitive operations
6. **Warnings**: Use `WithWarning()` for important caveats
7. **Next actions**: Use `WithNextActions()` for suggested follow-ups

## Example

```csharp
[AgentCapability("my_capability")]
public sealed class MyCapabilities
{
    [AgentAction("Do something important", RequiresApproval = true)]
    public Task<CapabilityResult> DoSomethingAsync(
        [AgentParam("The input value")] string input)
    {
        // Implementation
        return Task.FromResult(
            CapabilityResult.Success("Done!")
                .WithWarning("This action is irreversible")
                .WithNextActions("Review", "Undo"));
    }
}
```

### Example with ContextKey

```csharp
[AgentAction("Start a workflow run")]
public async Task<CapabilityResult> StartRunAsync(
    [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
    [AgentParam("Workflow name", Required = true)] string workflowName)
{
    // sessionId is auto-injected from runtime context — hidden from AI
}
```

## References

- See `src/AgentBlazor.Core/Attributes/` for attribute definitions
- See `demo/AgentBlazor.Demo/Services/` for usage examples
- See `.github/skills/ab-capability-authoring/` for detailed guidance on `[AgentParam]` and `ContextKey`
- See `.github/skills/ab-context-assembly/references/context-dictionary.md` for the runtime context keys available for `ContextKey` binding