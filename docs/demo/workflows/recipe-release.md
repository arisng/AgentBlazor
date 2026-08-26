# Recipe Release

> **Agent**: Recipe Release Agent | **Capability**: `DojoRecipeReleaseCapabilities` | **Route**: `/demo/workflows/recipe-release`

## What it is

A recipe validation and release workflow for the Dojo workspace. The agent validates
recipe data, drafts release notes, and manages the approval cycle. Demonstrates
`AgentForm` for data entry and structured release management.

## Why this matters

Releasing a new recipe (or product, or document, or configuration) involves multiple
steps: validate the data, draft the release notes, get approval, and publish. This
workflow shows how an agent can guide a user through each step, filling in forms,
checking validation rules, and pausing for approval at the critical moments. It is
a template for any "validate → draft → approve → publish" workflow — which covers a
huge range of business processes.

## Key features demonstrated

- `AgentForm` — recipe data entry and validation
- `AgentDataGrid` — recipe listing
- `AgentDialog` — release draft review
- Approval flows — release draft requires approval
- Structured outputs — validation results

## How to experience it

1. Navigate to `/demo/workflows/recipe-release`.
2. **View recipes**: type `Show all recipes`.
3. **Validate**: type `Validate the new recipe` — structured validation result.
4. **Draft release**: type `Prepare a release draft` — approval dialog appears.
5. **Release**: approve the draft → recipe is released.

## Prompts to try

- `Show all recipes`
- `Validate the new recipe`
- `Prepare a release draft`
- `Apply the recovery playbook`
- `Reset the workflow`

## What to observe

- Recipe data is entered through an `AgentForm`.
- Validation produces structured output with pass/fail fields.
- Release drafting requires approval before executing.
- The workflow follows a validate → draft → approve → release cycle.

## Related features

- [Form, Dialog & Select](../components/form-dialog-select.md)
- [Approval Flows](../capabilities/approval-flows.md)
- [Structured Outputs](../capabilities/structured-outputs.md)
