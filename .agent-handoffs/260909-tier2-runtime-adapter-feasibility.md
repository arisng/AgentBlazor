# Handoff: IAgentRuntimeAdapter Replacement for Tier 2 Dynamic Agent Registration

**Date:** 2026-09-09
**Source:** dprocess-dotnet-starter-kit repo (AgentChat domain modeling session)
**Target:** AgentBlazor repo
**Priority:** P1 — blocks Tier 2 user-customized agents

## Context

The FSH platform needs to implement **Tier 2 agents** — customer-facing agents where tenant users can customize:
1. **System instructions** (persona/personality per agent)
2. **Tool selection** (subset of available tools per agent)

AgentBlazor's current registration model is **startup-time static**:
- `WithInstructions(string)` — static string, no factory/delegate overload
- `WithAllowedActions(...)` — registration-time tool filter, baked into `AgentRegistration`
- `IAgentTurnMiddleware` — can modify context dictionary, but **cannot** modify system instructions or tool definitions per turn

## The Problem

We need **per-conversation dynamic customization** of both instructions and tools, but:
- Middleware (Approach 2) cannot touch instructions or tools
- Multiple `AddWorkflow` registrations (Approach C) are static at startup
- The only escape hatch is **replacing `IAgentRuntimeAdapter`** (Approach 3)

## What We Need the AgentBlazor Team to Audit

### 1. Can `IAgentRuntimeAdapter` be replaced per-agent (not globally)?

Current registration:
```csharp
builder.UseRuntimeAdapter<T>();  // global replacement
```

**Question:** Is there a per-agent overload? Can we register different adapters for different agents, or is it a single global replacement?

### 2. What does the decorator pattern look like in practice?

The `dynamic-instructions.md` skill shows a decorator pattern:
```csharp
public class InstructionDecoratorAdapter(
    IAgentRuntimeAdapter _inner,
    IUserContext _userContext) : IAgentRuntimeAdapter
{
    public Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request, CancellationToken ct = default)
    {
        request.Context ??= new Dictionary<string, string>();
        request.Context["custom.instructions"] = BuildDynamicInstructions(_userContext);
        return _inner.RunTurnAsync(request, ct);
    }
}
```

**Questions:**
- Does `AgentTurnRequest` expose the system instructions text for modification?
- Does `AgentTurnRequest` expose the tool list for filtering?
- Or does the adapter need to reconstruct the entire `ChatRequest` from scratch?

### 3. Can the adapter modify `ChatOptions.Tools` per turn?

The adapter calls the LLM via `IChatClient`. If the adapter constructs `ChatOptions`:
- Can it filter `ChatOptions.Tools` to a subset based on the agent definition?
- Is there a recommended pattern for tool projection/filtering?

### 4. What's the blast radius of replacing the adapter?

- Does replacing the adapter break `UseDevTools()`, `UseProLicense()`, conversation history, inspector recording?
- Is there a safe decorator that preserves all default behavior while allowing instruction/tool customization?

### 5. Performance implications

- If we replace the adapter globally but only customize for Tier 2 agents, what's the overhead on Tier 1 agents?
- Can the adapter detect the agent tier and skip customization for Tier 1?

## Proposed Investigation Steps

1. **Decompile** `AgentClientRuntimeAdapter` (or whatever the default adapter implementation is) to understand the full turn lifecycle
2. **Map** which `AgentTurnRequest` properties are mutable vs read-only
3. **Prototype** a `Tier2RuntimeAdapterDecorator` that:
   - Reads the agent definition from a scoped service
   - Injects custom instructions into the system prompt
   - Filters `ChatOptions.Tools` based on `EnabledToolIds`
   - Delegates everything else to the inner adapter
4. **Verify** that `UseDevTools()` and conversation history still work with the decorator
5. **Benchmark** the decorator overhead on Tier 1 agents (should be near-zero)

## Success Criteria

- A working prototype that demonstrates per-conversation instruction injection and tool filtering
- Confirmation that Tier 1 agents are unaffected
- Performance benchmark showing <1ms overhead per turn for the decorator path
- Documentation of any breaking changes or limitations

## Related Files

- `skills/ab-context-assembly/references/dynamic-instructions.md` — Approach 3 documentation
- `dprocess-dotnet-starter-kit/.github/skills/ab-tool-registration/SKILL.md` — Per-agent tool filtering via `WithAllowedActions`
- `skills/ab-middleware-authoring/SKILL.md` — Middleware capabilities and limitations
- `dprocess-dotnet-starter-kit/.docs/adr/0017-agent-tier-model.md` — Tier model (Tier 1 / Tier 2)
- `dprocess-dotnet-starter-kit/src/Modules/AgentChat/CONTEXT.md` — AgentChat domain contract
