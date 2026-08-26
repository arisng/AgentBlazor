# Release Dossier

> **Agent**: Release Dossier Agent | **Capability**: `ReleaseDossierCapabilities` | **Route**: `/demo/workflows/release-dossier`

## What it is

A release documentation workflow where the agent assembles a release dossier (a
bundled document package), manages review cycles, and handles release approval.
Demonstrates the guided journey pattern and approval-gated dossier assembly.

## Why this matters

Every release needs documentation — changelogs, migration guides, approval sign-offs,
and deployment checklists. Assembling these manually is tedious and error-prone. This
workflow shows how an agent can gather the pieces, assemble them into a structured
dossier, route them for review, and pause for approval before publishing. It
demonstrates that agents excel at the "collect, organize, and package" pattern —
work that is structured, repetitive, and benefits from automation while still
requiring human sign-off.

## Key features demonstrated

- `AgentDialog` — dossier assembly and review views
- Approval flows — dossier preparation requires approval
- Guided journey — multi-stage dossier assembly workflow

## How to experience it

1. Navigate to `/demo/workflows/release-dossier`.
2. **View items**: type `Show release items`.
3. **Assemble dossier**: type `Prepare release dossier` — approval dialog appears.
4. **Review**: type `Show dossier for review` — dialog with dossier contents.
5. **Approve release**: follow the guided journey to complete the release.

## Prompts to try

- `Show release items`
- `Prepare release dossier`
- `Show dossier for review`
- `Apply the recovery playbook`
- `Reset the workflow`

## What to observe

- The dossier assembly follows a structured multi-stage process.
- Approval is required before the dossier is finalized.
- The guided journey tracks progress through assembly stages.
- The dossier bundles multiple release artifacts into a single package.

## Related features

- [Approval Flows](../capabilities/approval-flows.md)
- [Structured Outputs](../capabilities/structured-outputs.md)
- [Response Orchestration](response-orchestration.md) — similar guided journey pattern
