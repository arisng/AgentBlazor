---
name: ab-demo-feature-overseer
description: >-
  Oversee and audit every demoable feature in the AgentBlazor.Demo project
  (demo/AgentBlazor.Demo) and maintain the empirical, evidence-gated features
  catalog. Use when auditing which AgentBlazor features are implemented in the
  Demo app, verifying whether a feature is actually wired (agents, workflow
  capabilities, agent actions, approvals, clarifications, agent-controllable
  components, chat surfaces, providers, inspector, middleware, logging, routes,
  launchpad scenarios), updating the Demo features catalog
  (references/demo-features-catalog.md), or extending the Demo with a new
  demoable feature. Runs the probe-based audit script
  (scripts/audit-demo-features.ps1) for evidence and routes implementation detail
  to the relevant ab-* skill (ab-agent-registration, ab-capability-authoring,
  ab-chat-composer, ab-chat-session-management, ab-context-assembly,
  ab-conversation-store, ab-entity-design, ab-in-chat-features, ab-inspector,
  ab-middleware-authoring, ab-mud-components, ab-provider-config, ab-testing).
  Triggers: "what features are demoed", "is feature X implemented in the Demo",
  "Demo features catalog", "audit the Demo project", "verify feature is wired".
metadata:
  version: 0.1.0
---

# Demo Feature Overseer

Oversees every demoable feature in the **AgentBlazor.Demo** project
(`demo/AgentBlazor.Demo`) and maintains an **empirical audit** of which features are
implemented, wired, or only library-supported. It drives that audit from actual Demo
source evidence and maps each feature to the ab\* skill that owns its implementation
detail.

## When to use

- **Auditing** — "what's implemented in the Demo?", "which features are demoed?",
  "is feature X actually wired?", "status of AgentBlazor features in the Demo app".
- **Maintaining the catalog** — adding, updating, or correcting the Demo features
  catalog (`references/demo-features-catalog.md`).
- **Verifying a feature claim** — confirming (with source evidence) that a feature is
  demoed before marketing/docs/tests rely on it.
- **Extending the Demo** — adding a new demoable feature and recording it.

## Workflow

### 1. Run the empirical audit

Generate fresh evidence from Demo source (never `bin`/`obj`):

```powershell
pwsh .github/skills/ab-demo-feature-overseer/scripts/audit-demo-features.ps1 -OutputPath artifacts/demo-audit.json
```

The script probes `Program.cs`, `Services/*.cs`, `Components/**/*.razor`, and
`appsettings.json` for wiring tokens and reports agents, workflow-capability classes,
agent actions, approval boundaries, clarifications, component usages, routes, scenario
catalog entries, middleware, log endpoints, providers, and config gates.

### 2. Read the evidence

Open `references/catalog-schema.md` for the status taxonomy and entry shape. Status is
**evidence-gated**: mark a feature `implemented` only when the current audit shows it
wired. `wired-dormant` covers code present but inactive (e.g. `UseDevTools` commented
out, provider key unset). `not-demoed` / `proposed` cover library features the Demo
does not yet show.

### 3. Consult the owning ab\* skill

Map each feature to its implementation-owning skill via `references/feature-skill-map.md`,
then load that ab\* skill's `SKILL.md` (from `.github/skills/`) before reasoning about
wiring. This is how the overseer delegates deep how-to knowledge to the existing ab\*
skills instead of duplicating it.

### 4. Update the catalog

Edit `references/demo-features-catalog.md` — add/update rows following the catalog
schema. Do **not** create extraneous files (no README/CHANGELOG here). Keep deep
procedural detail in the owning ab\* skills, not in this catalog.

### 5. Maintain the coverage matrix (living doc)

`references/demoable-coverage-matrix.md` is the living doc showing, for the full
spectrum of **demoable** AgentBlazor features, which the Demo actually implements
(✅ implemented / 🟡 wired-dormant / 🔶 partial / ⛔ not-demoed).

**Grounding rule**: a row counts as ✅/🔶 only when there is a **corresponding demo use
case actually wired in the Demo project** — never because the library supports it or a
default is active. If the Demo never exercises the feature, mark it ⛔. On every audit:

- refresh evidence by running the audit script, then grep the Demo for the wiring
  tokens of any 🟡/🔶/⛔ rows the script can't see (e.g. `AddTool`, `PredefinedPrompts`,
  `UseDevTools`, `[AgentParam]`);
- flip statuses only on that evidence and keep the per-area counts in sync;
- when a genuinely un-demoed feature gets wired, move it to ✅ and note the ab\* skill
  that owns it.

## Files

- `scripts/audit-demo-features.ps1` — probe-based empirical feature auditor.
- `references/demoable-coverage-matrix.md` — **living doc**: demoable features vs.
  Demo implementation coverage. This is the primary maintained artifact.
- `references/demo-features-catalog.md` — structured catalog of what the Demo already
  demos (status per feature with evidence + owning ab\* skill).
- `references/catalog-schema.md` — status taxonomy + catalog entry anatomy + how to
  read `agentRegistrations[]` evidence.
- `references/feature-skill-map.md` — maps each feature area to its ab\* owner skill.

## Rules

- Evidence over assumption: never mark `implemented` without current audit evidence.
- Focus on the Demo project only; never scan the AgentBlazor library/NuGet packages
  for Demo "features".
- Delegate implementation detail to ab\* skills; this skill stays an overseer + catalog.