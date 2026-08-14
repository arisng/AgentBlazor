# UAT Bucket — Agent & Capability Registration

> Aspect: agent registration and semantic-capability authoring (AgentBlazor).
> Status: **SCAFFOLD** — no test cases yet. Populate incrementally as `ab-agent-registration`
> and `ab-capability-authoring` features are worked.
> **Prefix:** `CAP-###` (bucket-local sequence; scaffolds start at `CAP-001` as cases land).
> Shared prerequisites: see `../runbook.md` once cases land (boot, personas, auth, evidence).

## Intended scope

Cases here assert that registered agents and capabilities behave correctly end-to-end:

- **Agent registration** (`ab-agent-registration`): `AddAgent` / `AddWorkflow`, route
  bindings (`WithRoutePrefixes`), component access (`WithAllowedComponents`), action
  allowlists (`WithAllowedActions`, `WithAllowedCapabilityActions`), data schemas
  (`WithDataSchemas`), instructions (`WithInstructions`).
- **Capability authoring** (`ab-capability-authoring`): `[AgentCapability]` classes,
  `[AgentAction]` methods, `[AgentParam]` parameters, `CapabilityResult` shape, approval
  boundaries (`RequiresApproval`), warnings/next-actions, `AddCapability` wiring.

## Example case shapes (draft — populate when the feature is exercised)

- Registration smoke: a registered agent + its workflow capability resolve from DI and
  appear in the route/action registry (`GET`/UI introspection) for the target tenant.
- Capability invocation: a `[AgentAction]` returns a well-shaped `CapabilityResult`
  (output, warnings, next-actions) and the approval boundary is honored.
- Scope enforcement: `WithAllowedActions` / `WithAllowedCapabilityActions` actually
  filters what a given agent may invoke.

> Placeholder — replace with concrete GIVEN/WHEN/THEN cases as feature work lands.
