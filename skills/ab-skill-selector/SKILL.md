---
name: ab-skill-selector
description: "Select the right consumer ab-* skill for an AgentBlazor goal, or chain multiple skills in the correct order. Use when a user asks to build, wire, debug, extend, or verify an AgentBlazor feature and you must decide which skill(s) in skills/ apply — e.g. adding an approval-gated capability, wiring tools/MCP, chat persistence, session browsing, session browser master-detail page, prompt alignment, provider config, middleware, multi-tenant setup, remote chat, runtime agent authoring (agent builder), UI-library coexistence, or CLI onboarding. Produces a selection plan (skill list, order, handoff notes), loads each skill's SKILL.md before acting, and abstains when no skill applies. Triggers: which skill, select a skill, choose a skill, chain skills, multi-skill goal, ab-* skill selection, AgentBlazor skill selection, AgentBlazor goal."
metadata:
  version: 0.2.1
---

# `ab-skill-selector` — Consumer Skill Selection & Chaining

Decides **which** consumer ab-* skill owns a user goal and — when the goal
spans several areas — **in what order** to chain them. It is the entry point
for AgentBlazor consumer work: it never re-implements what the owning skills
already know, it points to them and sequences their execution.

**Scope**: This skill covers the 20 consumer-facing ab-* skills shipped in the
AgentBlazor plugin (`skills/`), excluding this selector itself. Internal/contributor skills (testing, UAT,
release, fork sync, roadmap triage, demo auditing, entity design,
contribution) are not in the plugin and not covered here.

## Consumer skills inventory

| # | Skill | Purpose |
|---|-------|---------|
| 1 | `ab-agent-registration` | Register agents/workflows, route prefixes, allowed components/actions, data schemas |
| 2 | `ab-agent-builder` | Runtime agent authoring (agent builder): SQL Server-backed definitions, store-backed `IAsyncAgentRegistry` as the authoring surface |
| 3 | `ab-capability-authoring` | Author `[AgentCapability]`/`[AgentAction]`/`[AgentParam]` classes, `CapabilityResult`, approvals, outputs |
| 4 | `ab-tool-authoring` | Service tools, MCP servers, tool parameters, per-agent tool filtering |
| 5 | `ab-context-assembly` | System-prompt construction, runtime context injection, prompt tracing, runtime adapter |
| 6 | `ab-prompt-engineering` | Author/align `WithInstructions` with the registered surface |
| 7 | `ab-chat-composer` | Composer script loading, keyboard behavior, text retention |
| 8 | `ab-in-chat-features` | Approvals, clarifications, handoff, generated UI, chips, slash commands |
| 9 | `ab-chat-session-browser` | Master-detail session browser UI: list, detail, agent picker, new-chat draft lifecycle, deep-linking |
| 10 | `ab-chat-session-management` | Session browse/resume/hydrate from stored history |
| 11 | `ab-conversation-store` | `IConversationStore` implementations, incremental persistence, action history |
| 12 | `ab-entity-design` | EF Core entities, multitenancy columns, migrations |
| 13 | `ab-middleware-authoring` | `IAgentTurnMiddleware` authoring, cross-cutting concerns |
| 14 | `ab-inspector` | Agent Inspector, dev tools, run/event/prompt/state replay |
| 15 | `ab-provider-config` | Provider seam, `ConfigureChatOptions`, reasoning_effort pinning |
| 16 | `ab-multitenancy` | Multi-tenant production (Finbuckle, per-tenant providers/stores, BFF) |
| 17 | `ab-mud-components` | MudBlazor wrappers, generative UI blocks, controllable-component base classes |
| 18 | `ab-ui-integration` | Coexist with another UI library (Telerik, Radzen, Syncfusion, …) |
| 19 | `ab-remote-chat` | WASM remote chat (`MapAgentBlazorRemoteChat`, `AgentBlazor.Client`) |
| 20 | `ab-cli` | Onboard an existing app via CLI (analyze / scaffold / doctor / validate) |

## When to use

- **Single-skill goals** — the goal maps to one surface area; select the one
  owning skill and follow it (fast path).
- **Multi-skill goals** — the goal spans two or more areas (e.g. "add an
  approval-gated capability and align its prompt"); build a chain, load each
  skill in order, and hand off state between phases.
- **"Which skill?" questions** — the user asks which ab-* skill applies to a
  task; answer with the selection plan.
- **Ambiguous goals** — the goal could match several skills; disambiguate via
  the selection table and the goal's dominant intent.

## Selection protocol

1. **Parse the goal.** Identify the AgentBlazor surface areas involved:
   registration, capabilities/actions, tools/MCP, chat surface/composer,
   sessions, persistence, providers, middleware, prompts, components,
   multitenancy, remote chat.
2. **Classify.** One area → single-skill selection. Two or more → chain.
3. **Select skills.** Match each area to its skill via the selection table
   below; read `references/skill-catalog.md` for full per-skill signals and
   boundaries.
4. **Order the chain.** Apply the ordering rules below; check
   `references/chain-playbooks.md` for known multi-skill patterns first.
5. **Execute.** For each skill in order: load `skills/<name>/SKILL.md`,
   follow its instructions, then record a **handoff note** — what changed, which
   files, decisions made, open questions — before loading the next skill.

## Ordering rules (for chains)

- **Foundation before surface**: registration (`ab-agent-registration`) before
  capabilities/tools/components; those before prompts and in-chat UX.
- **Data before UI**: `ab-entity-design` → `ab-conversation-store` →
  `ab-chat-session-management` → `ab-chat-session-browser` → chat-surface skills.
- **Author before align**: `ab-capability-authoring` / `ab-tool-authoring`
  before `ab-prompt-engineering` (prompts must match the registered surface).
- **Setup before debug**: `ab-provider-config` / `ab-context-assembly` before
  `ab-inspector`-driven debugging.

## Selection table

| Goal area | Skill |
|-----------|-------|
| Register agents/workflows, route prefixes, allowed components/actions, data schemas, dynamic/per-tenant registries | `ab-agent-registration` |
| Runtime agent authoring (agent builder): create/edit/delete agents persisted to SQL Server, store-backed registry as the authoring surface | `ab-agent-builder` |
| Onboard an existing app via CLI (analyze / scaffold / doctor / validate, `.agentblazor/AGENT.md`) | `ab-cli` |
| Author `[AgentCapability]` / `[AgentAction]` / `[AgentParam]` classes, `CapabilityResult`, approvals, outputs, next actions | `ab-capability-authoring` |
| Service tools, MCP servers, tool parameters, per-agent tool filtering, tool dispatch | `ab-tool-authoring` |
| System-prompt construction, runtime context injection, prompt tracing, runtime adapter | `ab-context-assembly` |
| Author/align `WithInstructions` with the registered surface | `ab-prompt-engineering` |
| Composer script loading, keyboard behavior, text retention | `ab-chat-composer` |
| Approvals, clarifications, handoff approval, generated UI, chips, slash commands, stop button, dev tools | `ab-in-chat-features` |
| Session browse/resume/hydrate from stored history | `ab-chat-session-management` |
| Master-detail session browser UI: list panel, detail panel, agent picker, new-chat draft lifecycle, deep-linking | `ab-chat-session-browser` |
| WASM remote chat (`MapAgentBlazorRemoteChat`, `AgentBlazor.Client`) | `ab-remote-chat` |
| MudBlazor wrappers, generative UI blocks, controllable-component base classes | `ab-mud-components` |
| Coexist with another UI library (Telerik, Radzen, Syncfusion, …) | `ab-ui-integration` |
| `IConversationStore` implementations, incremental persistence, action history | `ab-conversation-store` |
| EF Core entities, multitenancy columns, migrations | `ab-entity-design` |
| `IAgentTurnMiddleware` authoring, cross-cutting concerns | `ab-middleware-authoring` |
| Agent Inspector, dev tools, run/event/prompt/state replay | `ab-inspector` |
| Provider seam, `ConfigureChatOptions`, reasoning_effort pinning | `ab-provider-config` |
| Multi-tenant production (Finbuckle, per-tenant providers/stores, BFF) | `ab-multitenancy` |

## Abstention

- **No AgentBlazor surface** in the goal (generic Blazor/.NET, unrelated topic)
  → answer directly; do not force a selection.
- **Goal names a skill that does not exist** (e.g. `ab-other-components`) →
  flag it; never invent its content.
- **No skill covers the goal** → say so explicitly instead of stretching a
  nearby skill.
- **Goal requires internal/contributor skills** (testing, UAT, release, fork
  sync, roadmap, demo audit, entity design, contribution) → those are not in
  the plugin; point the user to the repo's `.github/skills/` directory.

## Files

- `references/skill-catalog.md` — full per-skill selection detail: scope,
  selection signals, boundaries, related skills. Read when the table above is
  ambiguous.
- `references/chain-playbooks.md` — common multi-skill chains with order
  rationale and handoff notes. Read when the goal spans two or more areas.
- `evals/ab-skill-selector.evals.md` — test prompts for this skill.

## Rules

- Select, don't re-implement: always load the owning skill's `SKILL.md` before
  acting on its area.
- One handoff note per phase: state what changed and what the next skill needs.
- Keep the selection plan visible: state the skill list and order up front,
  then execute.
- Consumer skills only: this plugin ships 20 skills. Do not reference skills
  outside the plugin (`.github/skills/`) as if they were available.