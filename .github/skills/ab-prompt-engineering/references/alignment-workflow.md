# Alignment Workflow — From Inventory to an Authoritative Prompt

The runnable procedure for producing a system prompt that is **provably aligned** with an agent's registered surface. Two uses: (a) authoring a new prompt end-to-end, and (b) re-running the coverage half when surface area changes.

## Table of contents

1. [Phase 0 — Scope the agent](#phase-0--scope-the-agent)
2. [Phase 1 — Inventory the registered surface (evidence)](#phase-1--inventory-the-registered-surface-evidence)
3. [Phase 2 — Author the prompt prose](#phase-2--author-the-prompt-prose)
4. [Phase 3 — Cross-check coverage (close gaps, delete phantoms)](#phase-3--cross-check-coverage-close-gaps-delete-phantoms)
5. [Phase 4 — Verify with a probe turn](#phase-4--verify-with-a-probe-turn)
6. [Phase 5 — Lock in and prevent drift](#phase-5--lock-in-and-prevent-drift)

---

## Phase 0 — Scope the agent

Resolve which agent you are prompting, then record its registration to avoid guessing:

- Agent name as registered (`AddAgent(name, ...)` / `AddWorkflow<TCapability>(name, ...)`).
- Its allowed scopes: `WithAllowedComponents`, `WithAllowedActions`, `WithAllowedCapabilityActions`, `WithDataSchemas`. These define the closed set your prompt may mention. See [`ab-agent-registration`](../../ab-agent-registration/SKILL.md).
- Whether it is route-locked (`WithRoutePrefixes`); a locked agent should be prompted for that route's context.
- The `WithDescription(...)` text (passed as metadata, not system prompt) — keep it consistent with the prose you write.

**Exit criteria:** you can name the agent and enumerate exactly which components, actions, capabilities, tools, and schemas it may use.

### Zero-capability agents

An agent can be registered with no `[AgentCapability]` classes — it may only have component actions (`WithAllowedComponents`), service/MCP tools (`AddTool`/`UseMcpServer`), and generated-UI tools. In this case:
- Phase 1 still runs; the survey register will have an empty `Capabilities` list.
- Phase 2 skips the capability/action usage section and focuses on component actions (§4), tool usage (§5), approval boundaries (§6), and runtime-context consumption (§10).
- Phase 3 applies the same coverage passes, but scoped to components and tools instead of capabilities.

### Multi-agent with different surfaces

When multiple agents share a prompt template but have distinct `WithAllowedActions`/`WithAllowedComponents`/`WithAllowedCapabilityActions`, each agent needs its own aligned prompt. Do not use a single prompt for agents with different allowed surfaces — the alignment is per-agent. Run this workflow separately for each agent, comparing their registration scopes to determine which actions each prompt should describe.

---

## Phase 1 — Inventory the registered surface (evidence)

Build a machine-readable register with the bundled survey script, then enrich it with human context:

```powershell
# From the consumer app root
powershell -File .github/skills/ab-prompt-engineering/scripts/survey-agent-surface.ps1 `
    -SourceDir ./Components -OutFile ./agent-surface.json -Format Json
```

What the register must contain per item:

- **Capability** — capability ID, `[AgentAction]` ID, `Name`, `Description`, `RequiresApproval`, `FollowUp`, per-action `Instructions`, `[AgentParam]` name / `Required` / `AllowedValues` / `Description`.
   Ground truth: [`ab-capability-authoring`](../../ab-capability-authoring/SKILL.md).
- **Component** — `AgentId`/component id, exposed `[AgentAction]` ids (e.g. `filter`, `set_field`, `open`), and `[AgentReadable]` state names (e.g. `currentPage`, `isValid`).
   Ground truth: [`ab-mud-components`](../../ab-mud-components/SKILL.md).
- **Tool** — service/MCP tool names + parameter descriptors + which agents get them (via `WithAllowedActions`).
   Ground truth: [`ab-tool-registration`](../../ab-tool-registration/SKILL.md).
- **Gate** — every `RequiresApproval = true` action, every auto-clarifying required param (`[AgentParam(Required = true)]`), handoff approval policy.
   Ground truth: [`ab-in-chat-features`](../../ab-in-chat-features/SKILL.md).
- **Schema** — `WithDataSchemas(...)` names and the entities they expose.
   Ground truth: [`ab-agent-registration`](../../ab-agent-registration/SKILL.md) and `ab-context-assembly`.

Commit the generated register alongside the prompt. It becomes the diffable baseline for Phase 5.

**Exit criteria:** the register lists every callable action with its ID, its parameters, and whether it is approval-gated or auto-clarified — and nothing more.

---

## Phase 2 — Author the prompt prose

Draft the prose around the register, not from memory. For each registered action, produce a "when to call" rule and, where relevant, a "when never to call" rule. Encode every boundary (approval, clarification, generated-UI) with explicit instruction language. Follow the section-by-section guidance in [`prompt-content.md`](prompt-content.md) for structure and per-surface patterns.

**Exit criteria:** every row of the register is addressed in the prose, and the prose introduces no action ID that is absent from the register.

---

## Phase 3 — Cross-check coverage (close gaps, delete phantoms)

Apply two passes mechanically over the draft:

**Pass A — Close gaps (registered but unmentioned).** Every action in the register must be named at least once in the prompt. Silent actions are never used. If an action is registered but you do not want the agent to surface it unprompted, say so explicitly (e.g. "only call `show_open_tickets` when the user asks about open tickets") rather than omitting it.

**Pass B — Delete phantoms (mentioned but unregistered).** Strip any action, component, tool, schema, or approval claim that is not in the register. A phantom mention teaches the model to attempt something the runtime will reject. Grep for every backticked/camelCase ID in the draft and confirm it resolves to a register row.

Also confirm the **boundary claims** match the register exactly:
- A `RequiresApproval = true` action must be described as "propose and wait for user confirmation"; never "perform immediately".
- A `[AgentParam(Required = true)]` parameter must be described as mandatory, to trigger auto-clarification when absent.
- A non-approval action must *not* be described as requiring confirmation.

**Exit criteria:** set(actions mentioned in prose) == set(actions in register) and every approval/clarification claim matches the flag in the register.

---

## Phase 4 — Verify with a probe turn

A prompt is only aligned when observed. Enable the inspector and prompt tracing, then run representative probes:

1. **Enable evidence surfaces** — `UseDevTools()` (free) for the inspector panel, and `EnablePromptTracing(...)` per [`ab-context-assembly`](../../ab-context-assembly/SKILL.md) / inspect via [`ab-inspector`](../../ab-inspector/SKILL.md).
2. **Happy path** — one turn per core action set; confirm the model picks the right action and fills required parameters.
3. **Boundary probes** — ask the agent to do an approval-gated action and confirm it pauses for approval rather than claiming completion; ask for a required param and confirm it clarifies rather than guessing.
4. **Negative probe** — send a request that should NOT trigger an action; confirm the model abstains (no ghost invocation).
5. **Read the Inspector** — the `Prompt` tab shows exactly what was sent; the `Events` tab shows which actions ran, in what phase. Fix prose on any mismatch and re-run.

**Exit criteria:** probe turns invoke the correct actions with correct parameters, pause at the correct gates, and abstain when asked.

---

## Phase 5 — Lock in and prevent drift

- Store the prompt and its register baseline together (e.g. in a `prompts/` folder or alongside `agent-instructions.txt`).
- Version both. Re-run Phase 1 + Phase 3 whenever you add a capability, action, `[AgentComponent]`, tool, approval gate, or schema.
- Run the [`drift-audit.md`](drift-audit.md) procedure on additive changes — it is the fast "did this addition leave the prompt stale?" check.
- Codify per-action ALWAYS/NEVER rules into the prompt so future edits flow from a stable contract, not retro-fixes.

**Exit criteria:** the prompt + register are versioned together, and a documented cadence exists for re-auditing on additive change.