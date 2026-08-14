# UAT Bucket — CLI Tooling

> Aspect: `agentblazor` CLI — analyze / scaffold / doctor / validate (AgentBlazor.Cli).
> Status: **SCAFFOLD** — no test cases yet. Populate incrementally as `ab-cli` work lands.
> **Prefix:** `CLI-###` (bucket-local sequence; scaffolds start at `CLI-001` as cases land).
> Shared prerequisites: see `../runbook.md` once cases land (boot, personas, auth, evidence).

## Intended scope

Cases here assert that the `agentblazor` CLI behaves correctly against this repo's apps:

- **Analyze** — read-only app analysis report generation.
- **Scaffold** — baseline AgentBlazor wiring / multi-step workflow onboarding artifacts.
- **Doctor / validate** — check/validate an AgentBlazor install.
- **AGENT.md lifecycle** — `init` / `update` / `watch` on `.agentblazor/AGENT.md`.
- **Config** — `.agentblazorc` and provider environment-variable handling.

> Do not use for greenfield app creation or authoring `[AgentCapability]`/`[AgentAction]`
> attributes directly — those belong to the `agent-capabilities` bucket / AgentBlazor library.

## Example case shapes (draft — populate when the feature is exercised)

- Analyze: `agentblazor analyze` against the Lifeline project succeeds and emits a report
  artifact (assert exit code + artifact presence).
- Doctor: `agentblazor doctor` reports a healthy install (or a diagnosed, actionable issue).
- Validate: `agentblazor validate` passes on a correctly-wired app and fails (with a clear
  message) on a misconfigured one.

> Placeholder — replace with concrete GIVEN/WHEN/THEN cases as feature work lands.
