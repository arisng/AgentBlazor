---
name: ab-uat-spec
description: "Run the reusable AgentChat full-regression UAT spec against a live FSH environment, organized into focused per-aspect test-case buckets under the AgentBlazor library + AgentChat module. Use when running the AgentChat full-regression suite (51 cases), auditing AgentChat conversation persistence/resource-context behavior, or extending the AgentChat UAT playbook. Load with testing-manual-harness, aspire-cli, playwright-cli, and mssql-cli. Triggers: AgentChat UAT, full-regression UAT, conversation persistence audit, resource-context audit, refresh-persistence, session-tab refresh, browser-list staleness, AgentChatSessionBrowser, 260807-agentchat-full-regression-uat-spec, agentchat-regression-spec, agentchat-regression-bucket, UAT bucket."
metadata:
  version: 0.2.0
---

# AgentChat UAT Spec — AgentBlazor (Bucket-Organized)

Run the reusable AgentChat full-regression UAT (51 cases) against a live FSH Development
stack. The spec is organized into **focused per-aspect buckets** (`references/buckets/`)
scoped to the AgentBlazor library + AgentChat module, plus a single shared runbook of
cross-bucket prerequisites. Test cases are **added incrementally** to the bucket that
owns the aspect being worked — buckets with an empty scaffold are filled as features land.

This skill owns the runbook contract (boot sequence, port discovery, tenants × users,
OTP auth) and the bucket index; it does **not** duplicate tool syntax from companion
skills — read those for tool-level commands.

## Domain Architecture Constraints

Mandatory-read preamble before extending this spec with new cases. Every agent MUST
consult the Tier 1 source-of-truth docs below before proposing new test cases.

### Tenant isolation
- `ConversationSessions.TenantId` is the single scoping column. Cross-tenant SELECT returns zero rows.
- Root tenant admin manages tenant lifecycle only (profiles, subscription, infra provisioning).
  Root tenant has ZERO cross-tenant AgentChat data access.
- Four seeded tenants: `root` (platform), `vortex-labs` (integration), `meridian` (integration), `showroom` (demo).

### Auth model
- Browser sessions use cookie auth + `?tenant=` query param (CustomDomainTenantMiddleware).
- API calls use BFF pattern (cookie forwarded by Playwright.Api).
- Root admin (`admin@root.com`) manages tenants only — no AgentChat data access.

### Source-of-truth docs (MUST read before proposing new cases)
| Document | Location |
|---|---|
| Bounded context map | `CONTEXT-MAP.md` |
| AgentChat domain vocabulary | `src/Modules/AgentChat/CONTEXT.md` |
| Multitenancy isolation model | `src/Modules/Multitenancy/CONTEXT.md` |
| Architecture rules | `.github/instructions/fsh-architecture.instructions.md` |
| Module boundaries | `.github/instructions/fsh-modules.instructions.md` |
| Persistence/schema | `.github/instructions/fsh-persistence.instructions.md` |
| QA process sequencing / real-data mandate | `.github/skills/testing-manual-harness/SKILL.md` |
| Local dev boot / profile ports / secrets | `.docs/how-to/operations/local-dev-environment-runbook.md` |
| Aspire CLI workflows | `.github/instructions/aspire-operations.instructions.md` |

## When to use

- Running the AgentChat full-regression UAT (all 51 cases) against a live stack.
- Auditing a single aspect (e.g. conversation persistence / resource-context behavior) —
  read only that aspect's bucket + the runbook, not the whole spec.
- Extending the UAT playbook — add cases to the owning aspect bucket (or scaffold a new one).

## Required Context (progressive disclosure)

Load **only** what the task needs:

| When | Read |
|---|---|
| **Running any cases** | `references/runbook.md` — the shared runbook (boot, ports, tenants×users, auth flows, endpoint inventory, wire contracts, DB access, evidence conventions, execution order, exit criteria, command templates) |
| **Targeting one aspect** | `references/buckets/<aspect>.md` — that aspect's cases only |
| **Running the full suite** | `references/buckets/README.md` — the aspect index + then each populated bucket |
| Boot/stack | `.github/instructions/aspire-operations.instructions.md`; `aspire start --isolated` for worktrees; discover live ports via `aspire describe`, never hard-code |
| Browser evidence | `playwright-cli` skill — `open`/`goto`, `fill`, `click`, `snapshot`, `viewport`; never test scripts |
| DB evidence | `mssql-cli` skill — SQL snapshots against `localhost,53935` (SA, container `fsh-dev-sqlserver`) |
| Aspect remediation | the `ab-*` skill mapped to each bucket (see routing table below) |

## Route: aspect → bucket → remediation skill

Choose the bucket by **aspect**, then read it. `—` means no dedicated `ab-*` remediation skill.

| Aspect | Bucket file | Prefix | Cases now | ab-* remediation skill |
|---|---|---|---|---|
| Stack boot, ports, tenants×users, auth pre-flight | `buckets/environment.md` | `ENV-` | ENV-001..005 | — |
| Auth + permissions (401/403, roles, root) | `buckets/auth-permissions.md` | `AUTH-` | AUTH-001..006 | — |
| Chat surfaces, composer, widget send/reply | `buckets/chat-composer-ux.md` | `COMP-` | COMP-001 | `ab-chat-composer`, `ab-mud-components` |
| Browse/resume/hydrate, browser list | `buckets/session-management.md` | `SESS-` | SESS-001..007 | `ab-chat-session-management` |
| Persistence, fresh-scope rewrite, ResourceId integrity | `buckets/conversation-store.md` | `PERS-` | PERS-001..004 | `ab-conversation-store`, `ab-entity-design` |
| ResourceType registry, caller scoping | `buckets/scoping-registry.md` | `SCOP-` | SCOP-001..007 | — |
| Usage pipeline, idempotency, cost | `buckets/usage-pipeline.md` | `USG-` | USG-001..006 | — |
| BFF proxy store wiring, no-ops, handoff | `buckets/bff-proxy.md` | `PROX-` | PROX-001..006 | — |
| Tenant isolation, root, cross-tenant | `buckets/multitenancy-isolation.md` | `MTEN-` | MTEN-001..006 | `ab-multitenancy` |
| Responsive viewport | `buckets/responsive-viewport.md` | `VPRT-` | VPRT-001..003 | — |
| Agent/capability registration + authoring | `buckets/agent-capabilities.md` | `CAP-` | *(empty scaffold)* | `ab-agent-registration`, `ab-capability-authoring` |
| In-chat interactive features | `buckets/in-chat-features.md` | `FEAT-` | *(empty scaffold)* | `ab-in-chat-features` |
| Tool/MCP invocation, turn middleware | `buckets/tools-middleware.md` | `TOOL-` | *(empty scaffold)* | `ab-tool-registration`, `ab-middleware-authoring` |
| UI coexistence, theming | `buckets/ui-theming-integration.md` | `THEM-` | *(empty scaffold)* | `ab-ui-integration`, `ab-mud-components` |
| CLI analyze/scaffold/doctor/validate | `buckets/cli-tooling.md` | `CLI-` | *(empty scaffold)* | `ab-cli` |

> **ID convention:** each bucket owns one stable uppercase prefix (`ENV-`, `AUTH-`, `COMP-`,
> `SESS-`, `PERS-`, `SCOP-`, `USG-`, `PROX-`, `MTEN-`, `VPRT-`, `CAP-`, `FEAT-`, `TOOL-`,
> `THEM-`, `CLI-`) followed by a zero-padded, **bucket-local** 3-digit sequence starting at `-001`.
> No phase (`P1/P2/P3`) or medium (`UI/DB/API`) encoding in the ID — execution order lives in
> the runbook §10, and evidence subfolders carry the medium. A bucket growing renumbers only its
> own tail; no IDs migrate between buckets. Scaffolds pre-allocate their prefix (`CAP-001`, …).

## Consuming the spec

- **Single source of truth is the runbook + buckets**, not a dated handoff. The dated run
  record `.agent-handoffs/260807-agentchat-full-regression-uat-spec.md` is historical —
  read the runbook + buckets for the live contract.
- Run cases in the runbook §10 execution order (ENV → COMP/SESS → USG → SESS/PERS → PROX/PERS
  → AUTH → MTEN → VPRT/MTEN → SCOP → close-out); each block's preconditions come from the prior block.
- Ports are dynamic — resolve every URL from the running AppHost (`aspire describe` /
  `aspire ps`), never assume baseline 7030/7140/7142.
- Prefix every turn created during a run with `P3UAT-` so DB assertions scope only to that
  run. Never delete other tenants' or other users' data.

## Evidence conventions

- **Evidence root:** session-owned (`{session-state}/files/<pass>/qa/evidence/<slug>/`) — never inside the worktree.
- **Layout:** `api/` `ui/` `db/` `logs/`; file name = `{case-id}-{n}.{ext}`; screenshots PNG 1920×1080 full-page (viewport matrix only for responsive cases); DB evidence = query + result snapshot; log evidence must capture Lifeline at **Information** severity (the fresh-scope seed line is `LogInformation`).
- **Required artifacts:** `capture-manifest.json`, `screenshots-audit.json`, `defect-log.md`, `uat-verdict.md`.

## Key-cost budget

- Live OpenAI turns are capped at **~6–8 per run** (runbook §10/§11). `USG-001` is the
  canonical live turn and doubles as runtime evidence for the bff-proxy bucket.
- **`PERS-001` consumes one of the budgeted live turns** (browser Chat-tab message + real reply
  before refresh) — reuse the `USG-001` conversation where possible. **`SESS-007` reuses
  PERS-001's session resource** and does not add a live turn beyond PERS-001's.

## Incremental bucket-fill convention

The UAT grows by **filling the bucket that owns the aspect being worked**, not by
appending to a single spec file:

1. Identify the aspect of the feature/fix → map it to a bucket via the routing table.
2. Add cases to that bucket file (or scaffold a new one if no bucket owns the aspect).
3. Reference the runbook for shared preconditions — never duplicate them in the bucket.
4. Keep the `ab-*` remediation skill mapping in the routing table correct.
5. Update the bucket index (`buckets/README.md`) case-count column.

## Use-case-driven QA phases (pm-* capability pilots)

The in-platform PM-agent pilot established a **reusable evolution pattern** for QA-ing new
pm-* capabilities without growing the regression buckets arbitrarily: author a **use-case
baseline up front** (each real-life scenario = a test case), then codify it as integration +
browser QA. This is the recommended path for future pm-* capabilities.

### Contract
- Every capability pilot opens with a **UC table** (e.g. UC-1..UC-8): real-life scenario
  (human PO/stakeholder/CTO action) → codified test case.
- **Each UC = a test case.** Map deterministically:
  - Pure decision/validation logic → Unit tier (`src/Tests/UnitTests/Lifeline/Services/`).
  - Composed flows and wire contracts → Integration tier
    (`src/Tests/Integration.Tests/Session/` + `AgentChat/`,
    `[Collection(IntegrationTestGroup.Name)]`), **deterministic, no live LLM**.
  - Interactive, LLM-shaped outcomes → browser QA (UC-level, budgeted live turns).
- Capture flows (UC-7-style) must be composed-flow integration tests (action → capture →
  assert persisted artifact), NOT a re-verify of an older case.

### Evidence conventions (inherited)
Same as the spec: evidence under `{session-state}/files/<pass>/qa/evidence/<slug>/`,
`capture-manifest.json` + `screenshots-audit.json` + `defect-log.md` + `uat-verdict.md`.

### Key-cost budget
- Live OpenAI turns budgeted ~6–8 per pass. Browser UC cases reuse turns across
  sub-assertions (e.g. fold a multi-turn refinement into UC-1 instead of a separate case).
- Keep the composed-flow Integration UCs live-LLM-free so deterministic coverage is never
  budget-constrained.

## Exit criteria

1. All 51 cases **pass** — or blockers resolved (Backend Coder remediation) and re-tested green in the same run.
1b. For pm-* capability pilots: every UC in the pass's use-case baseline has a codified test
    case with evidence; deterministic UCs green, interactive UCs evidenced via browser QA.
2. `defect-log.md` has no open Blocker/High severity entries.
3. Evidence complete: every case has its required artifacts; `capture-manifest.json` + `screenshots-audit.json` present and consistent (counts match).
4. Quality gate green: `task ax:quality:strict` passes (pre-close validation).
5. `uat-verdict.md` records per-case pass/fail and routes any residual Medium/Low defects with owner + disposition.
6. Handoff (`PROX-006`) committed reflecting the UAT outcome.
