---
name: ab-prompt-engineering
description: "Craft and keep aligned AgentBlazor agent system prompts (WithInstructions) with registered surface — [AgentCapability]/[AgentAction]/[AgentParam], [AgentComponent] components, service/MCP tools, approval gates, clarifications, data schemas. Use when authoring, refining, or auditing a prompt for hallucinated/omitted/over-used actions, misaligned approval boundaries, or drift after adding capabilities/components/tools. Consumer-side only; never edit package internals. Triggers: system prompt, WithInstructions, align prompt, prompt alignment, agent prompt, refine agent prompt, craft agent prompt, hallucinated action, agent calls wrong action, prompt drift, prompt out of sync, prompt checklist, prompt survey, WithDescription, WithDataSchemas."
metadata:
  version: 0.1.0
---

# `ab-prompt-engineering` — System-Prompt Authoring & Alignment

Consumer-side guidance for **writing and keeping aligned** the system prompt (`WithInstructions(string)`) that drives an AgentBlazor agent. It exists so your prompt always matches the agent's *actual* registered surface — capabilities, workflow actions, components, tools, approval gates, and data schemas — rather than a stale approximation of them. Written for **consumer apps that reference the public AgentBlazor NuGet package**, not for the AgentBlazor source repo.

## Scope — what this skill touches

- **All changes happen only in the consumer app**: the string you pass to `WithInstructions`, and the app's own `[AgentCapability]`/`[AgentAction]`/`[AgentComponent]`/`AgentTool` definitions you align it against.
- **Never edit anything under `_content/AgentBlazor/...`** — those are read-only static assets served from the installed package.
- The AgentBlazor attributes are the *source of truth* for what the agent can do; your prompt is *prose about* that source of truth. This skill never instructs you to change package behavior to fit a prompt.
- You do **not** need the AgentBlazor source repo for anything in this skill.

## How the prompt relates to capability registration

AgentBlazor sends tool/action definitions to the LLM as **native function declarations** (NOT inside the system prompt). The system prompt governs *when and how* to use those tools, the tone, and the guardrails. Alignment is therefore a two-sided contract:

| Side | What it contains | Where it wins |
|---|---|---|
| **Tool definitions (registration)** | Action `Description`s, `[AgentParam]` shapes, required flags, `RequiresApproval`, `[AgentAction] Instructions` | Mechanically enforced by the runtime — the LLM can only call what is registered |
| **System prompt (this skill)** | When to call each action, in what order, what not to do, approval language, schema usage, runtime-context consumption | Governs judgment → wrong actions, omitted actions, and boundary violations live here |

A prompt that references an action ID that does not exist, or a behavior that no longer matches `RequiresApproval`, is **misaligned** even though the tool definitions are correct.

## Golden rules

1. **Ground every claim in a registered surface.** Every action you describe must exist as an `[AgentAction]`, `[AgentComponent]` action, or registered tool. Do not invent capabilities in prose.
2. **Respect approval and clarification boundaries.** If an action has `RequiresApproval = true`, your prompt must instruct the agent to stop and let the user review before the runtime executes it, and never to claim "done" for an awaiting-approval action. See `ab-in-chat-features`.
3. **Match action IDs exactly.** Refer to actions by their real `ActionId`/`capabilityId.actionId` form in prose. A mismatched ID trains the model toward a call that will fail or a wrong target.
4. **Prefer ALWAYS/NEVER clauses for judgment.** Use the imperative to encode when to act versus when to abstain — this maps to `[AgentAction] Instructions` and to `NeedsClarification`/`RequiresApproval`.
5. **Treat every prompt as a versioned, auditable artifact.** Re-run the alignment workflow whenever you (or the package) add a capability, component, action, approval gate, tool, or data schema. See `references/drift-audit.md`.
6. **Verify with the Inspector + prompt tracing.** A prompt is only "aligned" once you observe a probe turn invoking the right actions with correct parameters and pausing at the right gates. See `ab-inspector` and `ab-context-assembly`.

## The alignment workflow (author → reinforce → verify → prevent)

Run this end-to-end when writing a new prompt, and re-run `verify` + `prevent` whenever surface area changes.

1. **Inventory the registered surface** — what the agent can actually do. Back it with `scripts/survey-agent-surface.ps1` for evidence.
   - Capabilities + `[AgentAction]` methods: [`ab-capability-authoring`](../ab-capability-authoring/SKILL.md)
   - Components the agent can control (`WithAllowedComponents`) + their readable state: [`ab-mud-components`](../ab-mud-components/SKILL.md)
   - Service tools + MCP tools (`AddTool`, `UseMcpServer`) filtered via `WithAllowedActions`: [`ab-tool-registration`](../ab-tool-registration/SKILL.md)
   - Approval gates, clarifications, handoff: [`ab-in-chat-features`](../ab-in-chat-features/SKILL.md)
   - Agent registration surface (`WithInstructions`, `WithDescription`, `WithAllowedActions`, `WithDataSchemas`): [`ab-agent-registration`](../ab-agent-registration/SKILL.md)
2. **Author the prompt** — draft the prose around the inventory so every registered action has a "when to call" rule and every boundary is described. Follow [`references/prompt-content.md`](references/prompt-content.md).
3. **Cross-check the draft against the inventory** — close every gap and delete every phantom: action IDs present in prose but missing from registration, and registration present but never mentioned. Use [`references/alignment-workflow.md`](references/alignment-workflow.md) for the checklist.
4. **Verify with a probe turn** — enable dev tools / prompt tracing, run a representative prompt, and confirm the model invokes the correct actions with the correct parameters and pauses at the correct gates. See `ab-inspector` and `ab-context-assembly`.
5. **Prevent drift** — codify per-action rules, keep a versioned survey output, and schedule the [`references/drift-audit.md`](references/drift-audit.md) check on additive changes.

## Reference files

- [`references/alignment-workflow.md`](references/alignment-workflow.md) — the step-by-step checklist that turns inventory into a prompt and proves coverage (also used as the diff step).
- [`references/prompt-content.md`](references/prompt-content.md) — section-by-section drafting guidance and per-surface prose patterns (identity, when-to-call rules, approval language, schema usage, runtime-context consumption).
- [`references/drift-audit.md`](references/drift-audit.md) — a repeatable audit for detecting when the prompt references IDs/approvals/flags that no longer match registration.

## Bundled scripts

- [`scripts/survey-agent-surface.ps1`](scripts/survey-agent-surface.ps1) — scans consumer source for `[AgentCapability]`, `[AgentAction]`, `[AgentParam]`, `[AgentComponent]`, and `AddTool` registrations and emits a machine-readable register (JSON by default) you can diff across prompt revisions. This is the mechanical backbone of the inventory step.

## Related skills

- [`ab-agent-registration`](../ab-agent-registration/SKILL.md) — how `WithInstructions`/`WithDescription`/`WithDataSchemas` fit into agent registration; route locking and allowed scopes.
- [`ab-capability-authoring`](../ab-capability-authoring/SKILL.md) — authoring `[AgentCapability]`/`[AgentAction]`/`[AgentParam]`; the actions your prompt must describe, their approval flags and `Instructions`.
- [`ab-mud-components`](../ab-mud-components/SKILL.md) — component actions + readable state your prompt can reference (e.g. "filter the grid", "read currentPage").
- [`ab-tool-registration`](../ab-tool-registration/SKILL.md) — service/MCP tools your prompt may instruct the agent to use.
- [`ab-in-chat-features`](../ab-in-chat-features/SKILL.md) — approval, clarification, handoff, generated-UI boundaries your prompt must describe correctly.
- [`ab-context-assembly`](../ab-context-assembly/SKILL.md) — *how* the prompt is sent and traced; the mechanical seam under this skill's prose.
- [`ab-middleware-authoring`](../ab-middleware-authoring/SKILL.md) — how to implement `IAgentTurnMiddleware` for cross-cutting context enrichment that the prompt should instruct the agent to use.
- [`ab-inspector`](../ab-inspector/SKILL.md) — the evidence view (Runs / Events / Prompt / State / Components) used in the verify step.