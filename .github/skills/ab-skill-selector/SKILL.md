---
name: ab-skill-selector
description: "Select the right ab-* skill for an AgentBlazor goal, or chain multiple ab-* skills in the correct order to achieve a multi-part goal. Use when a user asks to build, wire, debug, extend, or verify an AgentBlazor feature and you must decide which skill(s) in .github/skills apply — e.g. adding an approval-gated capability, wiring tools/MCP, chat persistence, session browsing, prompt alignment, provider config, middleware, multi-tenant setup, remote chat, UI-library coexistence, CLI onboarding, Demo work, testing/UAT, release, fork sync, or roadmap triage. Produces a selection plan (skill list, order, handoff notes), loads each skill's SKILL.md before acting, and abstains when no skill applies. Triggers: which skill, select a skill, choose a skill, chain skills, multi-skill goal, ab-* skill selection, AgentBlazor skill selection, AgentBlazor goal."
metadata:
  version: 0.1.0
---

# `ab-skill-selector` — Skill Selection & Chaining

Decides **which** ab-* skill owns a user goal and — when the goal spans several
areas — **in what order** to chain them. It is the entry point for AgentBlazor
work: it never re-implements what the owning skills already know, it points to
them and sequences their execution.

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
   multitenancy, remote chat, demo, testing, release, fork, roadmap.
2. **Classify.** One area → single-skill selection. Two or more → chain.
3. **Select skills.** Match each area to its skill via the selection table
   below; read `references/skill-catalog.md` for full per-skill signals and
   boundaries.
4. **Order the chain.** Apply the ordering rules below; check
   `references/chain-playbooks.md` for known multi-skill patterns first.
5. **Execute.** For each skill in order: load `.github/skills/<name>/SKILL.md`,
   follow its instructions, then record a **handoff note** — what changed, which
   files, decisions made, open questions — before loading the next skill.
6. **Verify.** If the goal includes verification, end the chain with
   `ab-testing` (unit/integration), `ab-uat-spec` (full regression), or
   `ab-demo-feature-overseer` (Demo evidence).

## Ordering rules (for chains)

- **Foundation before surface**: registration (`ab-agent-registration`) before
  capabilities/tools/components; those before prompts and in-chat UX.
- **Data before UI**: `ab-entity-design` → `ab-conversation-store` →
  `ab-chat-session-management` → chat-surface skills.
- **Author before align**: `ab-capability-authoring` / `ab-tool-authoring`
  before `ab-prompt-engineering` (prompts must match the registered surface).
- **Setup before debug**: `ab-provider-config` / `ab-context-assembly` before
  `ab-inspector`-driven debugging.
- **Verify last**: `ab-testing`, `ab-uat-spec`, `ab-demo-feature-overseer` end
  the chain.
- **Process skills are terminal**: `ab-release`, `git-fork-sync`,
  `roadmap-triage`, `ab-contribution` wrap the technical work they cover.

## Selection table

| Goal area | Skill |
|-----------|-------|
| Register agents/workflows, route prefixes, allowed components/actions, data schemas, dynamic/per-tenant registries | `ab-agent-registration` |
| Onboard an existing app via CLI (analyze / scaffold / doctor / validate, `.agentblazor/AGENT.md`) | `ab-cli` |
| Author `[AgentCapability]` / `[AgentAction]` / `[AgentParam]` classes, `CapabilityResult`, approvals, outputs, next actions | `ab-capability-authoring` |
| Service tools, MCP servers, tool parameters, per-agent tool filtering, tool dispatch | `ab-tool-authoring` |
| System-prompt construction, runtime context injection, prompt tracing, runtime adapter | `ab-context-assembly` |
| Author/align `WithInstructions` with the registered surface | `ab-prompt-engineering` |
| Composer script loading, keyboard behavior, text retention | `ab-chat-composer` |
| Approvals, clarifications, handoff approval, generated UI, chips, slash commands, stop button, dev tools | `ab-in-chat-features` |
| Session browse/resume/hydrate from stored history | `ab-chat-session-management` |
| WASM remote chat (`MapAgentBlazorRemoteChat`, `AgentBlazor.Client`) | `ab-remote-chat` |
| MudBlazor wrappers, generative UI blocks, controllable-component base classes | `ab-mud-components` |
| Coexist with another UI library (Telerik, Radzen, Syncfusion, …) | `ab-ui-integration` |
| `IConversationStore` implementations, incremental persistence, action history | `ab-conversation-store` |
| EF Core entities, multitenancy columns, migrations | `ab-entity-design` |
| `IAgentTurnMiddleware` authoring, cross-cutting concerns | `ab-middleware-authoring` |
| Agent Inspector, dev tools, run/event/prompt/state replay | `ab-inspector` |
| Provider seam, `ConfigureChatOptions`, reasoning_effort pinning | `ab-provider-config` |
| Multi-tenant production (Finbuckle, per-tenant providers/stores, BFF) | `ab-multitenancy` |
| Unit/integration tests (xUnit, bUnit, coverlet) | `ab-testing` |
| AgentChat full-regression UAT (51 cases) | `ab-uat-spec` |
| Demo feature audit + evidence-gated catalog | `ab-demo-feature-overseer` |
| Contributor workflows, PRs, coding standards | `ab-contribution` |
| Release, versioning, publishing, private feeds | `ab-release` |
| Fork upstream sync (mirror/merge model) | `git-fork-sync` |
| Issue/PR/CI triage against the committed roadmap | `roadmap-triage` |

## Abstention

- **No AgentBlazor surface** in the goal (generic Blazor/.NET, unrelated topic)
  → answer directly; do not force a selection.
- **Goal names a skill that does not exist** (e.g. `ab-other-components`) →
  flag it; never invent its content.
- **No skill covers the goal** → say so explicitly instead of stretching a
  nearby skill.

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