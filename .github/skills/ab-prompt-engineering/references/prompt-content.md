# Prompt Content — Section-by-Section Drafting Guidance

How to write the `WithInstructions(string)` prose so that every registered capability, workflow action, component, tool, approval gate, and schema is described in a way the model will follow and the runtime will honor. Use after the inventory (Phase 1) and before the coverage cross-check (Phase 3) in [`alignment-workflow.md`](alignment-workflow.md).

## Section map

A well-aligned agent system prompt has distinct sections. Draft them in order.

| # | Section | Purpose | Source surface |
|---|---|---|---|
| 1 | Identity & mission | Who the agent is, its boundary | `WithDescription`, agent name |
| 2 | Operating rules | Tone, honesty, abstain-when-unsure, when to clarify | — |
| 3 | Capability/action usage | One rule per `[AgentAction]`: when to call, in what order, required params | `[AgentCapability]`/`[AgentAction]`/`[AgentParam]` |
| 4 | Component authors & state | Which `[AgentComponent]`s the agent drives and what readable state it consults | `WithAllowedComponents`, `[AgentReadable]` |
| 5 | Tool usage | Service/MCP tools the agent may invoke | `AddTool`, `UseMcpServer`, `WithAllowedActions` |
| 6 | Approval & confirmation | Exactly which actions require the user to approve before execution | `RequiresApproval`, built-in wrapper confirmations |
| 7 | Clarification | When to ask instead of guess | `[AgentParam(Required = true)]`, `NeedsClarification` |
| 8 | Generated-UI & output shaping | How to emit cards/forms/tables, next actions, warnings | `CapabilityResult.WithOutput/WithNextActions/WithWarnings`, `EnableGeneratedUi` |
| 9 | Schema usage | How to read the auto-appended data schemas | `WithDataSchemas` |
| 10 | Runtime context consumption | Reading the "Runtime context:" block injected per turn | `AgentRuntimeContextKeys`, middleware |

Keep the prose imperative and scannable. Where a section is empty for your agent (e.g. no tools), omit it rather than writing "none".

---

## 1. Identity & mission

State who the agent is and what it may do, bounded by its registration. Keep the mission consistent with `WithDescription` (which is agent metadata, not prose, via [`ab-agent-registration`](../../ab-agent-registration/SKILL.md)).

> You are the Support Agent. You help staff triage, draft, and dispatch support tickets. You may only take actions listed below; if a request needs an action you cannot perform, say so and stop.

## 2. Operating rules

Encode honesty and restraint. ALWAYS/NEVER clauses are the strongest form:

- ALWAYS read the "Runtime context:" section before acting.
- NEVER claim an action succeeded before it executed; never claim a pending approval is done.
- When you lack a required value, ask for it (see Clarification) instead of inventing one.
- If a request is out of scope, refuse and state the boundary.

## 3. Capability / action usage (per `[AgentAction]`)

For each action in the register produce a "when to call" rule. Use the action's real ID (from `[AgentAction]` or `capabilityId.actionId`) and its `[AgentParam]` names:

> - `support_inbox.show_open_tickets` — call when the user asks to see open/supporting tickets. Required param `ticketScope`. Only call when `ticketScope` is provided or during auto-clarification.
> - `support_inbox.draft_reply` — call to prepare a reply draft. ALWAYS require explicit ticket context; NEVER send a draft without the user reviewing it (this action requires approval).

Mirror the `[AgentAction] Instructions` field when present — per-action behavioral guidance already carries ALWAYS/NEVER language (see [`ab-capability-authoring`](../../ab-capability-authoring/SKILL.md)).

## 4. Component authoring & state

When the agent can control components (`WithAllowedComponents`), its prose should describe which component it may drive and which readable state it consults (via [`ab-mud-components`](../../ab-mud-components/SKILL.md)):

> - You may filter the `suppliers-grid` (`AgentDataGrid`) via its `filter` action and read its `currentPage` and `filters` state.

## 5. Tool usage

Describe service/MCP tools the agent may invoke and any ordering (via [`ab-tool-registration`](../../ab-tool-registration/SKILL.md)):

> - `lookup_ticket(ticketId, includeNotes)` — use to hydrate ticket details before composing a reply.

## 6. Approval & confirmation

The single most misaligned section. Only actions with `RequiresApproval = true` (plus built-in wrapper confirmations) must be described as pausing for the user. For each such action:

> - `draft_reply` requires your approval. Propose the draft, then stop and wait for the user to Approve or Deny. Do not summarize a result until it is approved.

For `[AgentComponent]` wrappers whose actions are confirmation-bound (e.g. `AgentDialog.Confirm`, `AgentForm.Submit`, `AgentNavMenu` navigation), describe them as requiring the user's confirmation in the same way. See [`ab-in-chat-features`](../../ab-in-chat-features/SKILL.md).

## 7. Clarification

Describe when the agent asks instead of guesses. `[AgentParam(Required = true)]` triggers an automatic clarification when absent:

> - Before calling `show_open_tickets`, ask for `ticketScope` if it was not provided — do not assume.

For free-form `NeedsClarification` scenarios, spell out the question style.

## 8. Generated-UI & output shaping

If generated UI is enabled (`EnableGeneratedUi="true"` on `AgentChatSurface` or `AgentChatWidget`), describe what the agent may render and the structured outputs it should attach (see `ab-in-chat-features`):

> - When listing tickets, attach `ticketCount` and `highlightedTicketIds` via `WithOutput`, and offer `WithNextActions` such as "Draft a reply".

## 9. Schema usage

When the agent has `WithDataSchemas(...)`, the package appends formatted schema docs to the system prompt (see [`ab-context-assembly`](../../ab-context-assembly/SKILL.md)). Your prose should point at them:

> - Read the READ-SAFE DATA SCHEMAS section when you need entity context. Treat them as reference data, not as writeable state.

## 10. Runtime context consumption

Direct the agent to act on the per-turn "Runtime context:" block injected into the user message (keys via `AgentRuntimeContextKeys` / middleware, in `ab-context-assembly`):

> - ALWAYS check "Runtime context:" for `user_role` and `timezone` and restrict actions accordingly.

---

## Anti-patterns to avoid

- **Review-copy drift** — describing an approval gate or action set from an older prompt instead of the current register. Re-derive from the survey output each time.
- **Phantom action IDs** — name an action not present in registration. Run the coverage pass (Phase 3) to delete these.
- **Over-authority** — prose that grants the agent powers beyond registration (e.g. "you can approve drafts yourself" when `RequiresApproval` is set).
- **Under-description** — registered actions left unmentioned, which the model will rarely surface.
- **Tone vs. fact** — producing a great-sounding prompt that does not match real action IDs / param names. The register is the single source of truth for names.