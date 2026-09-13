# Handoff: Agent registration & tool-surface gaps surfaced by the FSH consumer

**Date:** 2026-09-10
**Source:** `dprocess-dotnet-starter-kit` worktree `feature-260909-agent-definition-tool-registry` (AgentChat consumer session, package `0.2.24-internal.3`)
**Target:** AgentBlazor repo (`develop`)
**Priority:** P1 — two allow-all fallbacks silently widen an agent's projected tool surface; a third registration trap silently discards agent instructions.

This session fixed the consumer side of three library behaviors by working **around** them (all workarounds verified live end-to-end). Each workaround is a signal that the library needs a first-class expression. Every claim below was confirmed by decompiling `AgentBlazor.Core.dll` 0.2.24-internal.3 (`ilspycmd`), not inferred.

## Gap 1 — `AddAgent` is replace-by-name with NO duplicate signal

Decompiled `AgentBlazor.Services.AgentBlazorBuilder.AddAgent`:

```csharp
_store.AgentRegistrations.RemoveAll(r => string.Equals(r.Name, registration.Name, StringComparison.OrdinalIgnoreCase));
_store.AgentRegistrations.Add(registration);
```

Last registration wins, case-insensitively, **silently**. The FSH consumer registered `"Lifeline Assistant"` twice — once in code with a hand-authored system prompt, once from a config-driven loop — and the config entry (empty `CustomInstructions`) discarded the code prompt. The agent then answered "Who are you?" with PM-flavoured capabilities its prompt explicitly forbade.

**Enhancement candidates (library):**
- Throw (or log a startup warning) when `AddAgent`/`AddWorkflow` is called with a name that already exists, unless an explicit opt-in (`allowReplace: true` or an options flag) is set.
- Or document the replace semantics prominently in `PACKAGE_README.md` + `agentblazor doctor` warning.

Consumer workaround for reference: `FSH repo` → `src/Playground/Playground.Lifeline/Services/AgentChat/Tier2AgentRegistrationFilter.cs` — a pure filter that drops config definitions whose names collide with code-registered agents, plus tests in `src/Tests/UnitTests/Lifeline/Services/AgentChat/Tier2AgentRegistrationFilterTests.cs`. It exists only because the library gives no explicit precedence model.

## Gap 2 — `AllowedComponents.Count == 0` means ALLOW ALL; deny-all is inexpressible

Decompiled `AgentBlazor.Components.ComponentActionPolicy.EvaluateAllowedCapabilities`:

```csharp
bool flag = allowedComponents.Count == 0 || allowedComponents.Contains(component.ComponentId);
```

Empty set == allow **all** catalog components. `AgentRegistrationBuilder.WithAllowedComponents()` with no arguments leaves the HashSet empty — identical to unset. So a consumer **cannot express "project zero component tools"** for an agent that must be data/service-tools only.

The shipped catalog (`DefaultShippedComponents.CreateCatalog` + `AgentComponentCapabilityProfile.Apply`) injects the full component action set (chat widget/panel, data grid, dialog, form, nav menu, select, autocomplete, date pickers, tree view, stepper, command bar, file upload, tabs). A standalone agent inherits all of it — the assistant advertised "open/close dialogs, navigate routes, switch tabs, submit forms…" as tools it didn't conceptually have.

**Enhancement candidates:**
- A public per-agent "no components" expression: e.g. `agent.WithNoComponents()` or a documented `AgentToolSurfacePolicy`-style sentinel **owned by the package** (the consumer invented `AgentToolSurfacePolicy.NoComponentActions = "__no_component_actions__"` in `FSH repo → src/Playground/Playground.Lifeline/Services/AgentChat/AgentToolSurfacePolicy.cs` — a non-empty set matching no real component id is currently the ONLY way to deny all).
- Or change the policy to a tri-state (unset = all; explicit empty = none) — would need a breaking-change evaluation against `DefaultAgentOptions.AllowedComponents` legacy behavior.
- Note the deprecated-`DefaultAgentOptions` path already has `ComponentCatalogMode` for the *catalog build*; there is no per-registration equivalent.

## Gap 3 — standalone agents inherit ALL capability `[AgentAction]`s (allow-all fallback)

Decompiled `AgentBlazor.Core.Runtime.Adapters.ChatClientRuntimeAdapter`:

```csharp
private bool IsCapabilityToolAllowed(AgentRegistration registration, string actionId)
{
    if (registration.AllowedCapabilityActions.Count > 0)
        return registration.AllowedCapabilityActions.Contains(actionId);
    return IsNonComponentToolAllowed(registration, actionId);   // → true while AllowedActions empty
}
```

And `WithAllowedCapabilityActions` is **internal** (only `AddWorkflow` seeds it). Therefore any standalone `AddAgent` gets every `[AgentAction]` of **every** registered capability class projected as a tool. In the FSH app the "Lifeline Assistant" advertised the Product Manager's backend actions ("brainstorm", "release-note lookup", "artifact creation") when asked to list its tools — the model reads tool descriptors, so awareness of other agents' capabilities is a real cross-agent leak, not just prompt noise.

**Consumer workaround (works, but is a hack the library should make unnecessary):** a zero-action anchor capability class — `FSH repo → src/Playground/Playground.Lifeline/Services/AgentChat/NoCapabilityActions.cs`. Its single action is permanently availability-gated (`[AgentAction(..., AvailableWhen = nameof(Disabled))]` with `private bool Disabled => false`), and `ReflectionAgentCapabilityRegistry.BuildDescriptor` drops unavailable actions before projection, so it contributes ZERO tools while making `AllowedCapabilityActions` non-empty (thereby excluding every other capability class). Registered via `AddWorkflow<NoCapabilityActions>(agentName, ...)`.

**Enhancement candidates:**
- Make `WithAllowedCapabilityActions` public (or add `agent.WithNoCapabilityActions()` / `agent.WithBackendCapabilityPolicy(BackendCapabilityPolicy.None)`).
- Consider **deny-by-default** for standalone agents (capability tools opt-in only) — matching the component-surprise pattern in Gap 2; if the change is too breaking, at minimum log a startup warning when a standalone agent projects capability actions it did not explicitly allow.
- The `AvailableWhen` availability-gate mechanism worked exactly as designed (verified: gated-off action projects zero tools) — no change needed there; it is just currently the only lever.

## What already works well (no action)

- `ComponentActionPolicy` per-agent component allowlists (once non-empty) + per-action `RequiresApproval`.
- `AddWorkflow<TCapability>` action-allowlist seeding — correct once the anchor trick is in place.
- Prompt composition with no instructions → `chatOptions.Instructions` unset (expected); the consumer-side prompt-fence discipline (`LifelineAssistantPrompt`) holds once the prompt actually reaches the model.
- `AgentRuntimeContextKeys.SessionId/RunId` context-key binding; `AgentTurnContext`/`IAgentTurnMiddleware` order (the consumer's `AgentNameCaptureMiddleware` at 0th position is a clean seam).

## Related handoffs (do not duplicate; read first)

- `.agent-handoffs/260909-tier2-runtime-adapter-feasibility.md` — the `IAgentRuntimeAdapter` replacement/decorator questions for dynamic Tier 2 instructions+tools. Gaps 1–3 above are the *static-registration* layer; that handoff covers the *per-turn dynamic* layer. A unified registration story (static allow/deny + dynamic override) should be designed together.
- `.agent-handoffs/260819-agentblazor-improvements.md` — gpt-5.6 `reasoning_effort` onboarding gap (separate track).
- Consumer-side context lives in the FSH repo (all paths under worktree `feature-260909-agent-definition-tool-registry`, branch `feature/260909-crud-endpoints-for-agent-tool-registry`, currently uncommitted): consumer fix wiring in `src/Playground/Playground.Lifeline/Program.cs` (agent-name constants, config-driven Tier 2 loop through the filter, `AddWorkflow<NoCapabilityActions>` + sentinel for the assistant and Tier 2 agents), regression tests under `src/Tests/UnitTests/Lifeline/Services/AgentChat/` (`NoCapabilityActionsTests.cs` locks the anchor contract: exactly one action, permanently unavailable, sentinel not a real component id).

## Live evidence the workarounds hold (for regression baselines)

Development/Showroom isolated stack, 2026-09-10: "List all your tools" on the Lifeline Assistant returned **exactly** its 7 service tools (`lookup_lifelines`, `lookup_lifeline`, `lookup_sessions`, `lookup_session`, `lookup_harvests`, `lookup_harvest`, approval-gated `save_harvest`); zero component-action mentions; zero PM capability mentions. PM agent regression-checked intact. 69/69 AgentChat unit tests green in `UnitTests.Lifeline`.

## Suggested skills for the next session

- `ab-agent-registration` — for designing the precedence/deny-all public API surface.
- `ab-capability-authoring` — for the `AvailableWhen` gate semantics and whether a "zero-action capability" should be a supported concept.
- `dotnet-inspect` — to re-verify surfaces on whatever next internal build is produced.
- `ab-local-nuget-install` — to land the enhancements in a new `0.2.24-internal.N` and re-verify the FSH consumer against the package (the FSH consumer's workarounds can then be deleted or reduced to thin calls into the new API).
