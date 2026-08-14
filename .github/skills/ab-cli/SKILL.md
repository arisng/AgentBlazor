---
name: ab-cli
description: "Drive the agentblazor CLI tool (AgentBlazor.Cli NuGet package) to wire AgentBlazor into existing Blazor apps. Use when the user wants to onboard an existing .NET/.sln/.slnx Blazor solution into AgentBlazor, generate a read-only app analysis report (analyze), scaffold baseline AgentBlazor wiring or multi-step workflow onboarding artifacts (scaffold / scaffold workflows), check or validate an AgentBlazor install (doctor / validate), initialize or regenerate .agentblazor/AGENT.md (init / update / watch), or configure .agentblazorc and provider environment variables. Do not use for greenfield AgentBlazor app creation or for authoring [AgentCapability]/[AgentAction] attributes directly — those use the AgentBlazor library and Starter sample instead."
metadata:
  version: 0.1.0
---

# AgentBlazor CLI

The `agentblazor` CLI is the advanced setup path for wiring AgentBlazor into **existing** Blazor apps. It analyzes `.sln`/`.slnx` solutions, scaffolds wiring and workflow artifacts, and keeps `.agentblazor/AGENT.md` current. Read-only commands never modify app code; mutating commands apply changes only with an explicit approval flag.

## Install

```bash
dotnet tool install --global AgentBlazor.Cli
```

Pin the version for reproducibility: `dotnet tool install --global AgentBlazor.Cli --version 0.2.22`.

## Choose the command

| Goal | Command |
|---|---|
| First-time onboarding of an existing app | `init` → `scaffold --diff` → `scaffold --approve` → build → `doctor` → `validate` |
| Read-only report of what the app exposes to an agent | `analyze` |
| Baseline wiring preview/apply | `scaffold` |
| Propose multi-step workflows + SOUL/skills | `scaffold workflows` |
| Verify baseline wiring | `doctor`, `validate` |
| Regenerate AGENT.md after code changes | `update`; auto-refresh while editing: `watch` |
| Multi-tenant onboarding (existing solution) | see the **Multi-Tenant Solutions** section below |

The default install story for new apps is `dotnet add package AgentBlazor` plus manual runtime wiring; use the CLI for existing solutions.

## Command overview

| Command | Purpose | Key options | Exit codes |
|---|---|---|---|
| `init [path]` | Create `.agentblazor/AGENT.md`, state, and optional `.agentblazorc`; show next installer steps | `--host`, `--description`, `--agent-goals`, `--save-config`, `-y` | 1 if no project found |
| `analyze [path]` | Read-only markdown analysis report | `--host`, `--output`, `--static-only`, `--scan-scope`, `--no-readiness`, `-y` | 1 if no project found |
| `scaffold [path]` | Preview/apply baseline wiring | `--host`, `--provider`, `--diff`, `--approve`, `--dry-run`, `--use-local-source` | 2 if install blocked |
| `scaffold workflows [path]` | Workflow onboarding (see reference) | `--description`, `--agent-goals`, `--workflow`, `--reject`, `--pin`, `--reviewed-by`, `--approve`, `--non-interactive` | — |
| `doctor [path]` | Readiness checks for baseline wiring | `--host`, `-y` | 0 pass / 2 missing / 1 error |
| `validate [path]` | Readiness + scaffold audit data | `--host`, `-y` | 0 ok / 2 blocking / 1 error |
| `update` | Regenerate AGENT.md if code changed | `--force`, `-y`, `--quiet` | 1 if not initialized |
| `watch` | Watch host project and auto-regenerate | `--debounce <ms>` (default 500) | 1 if not initialized |

**Multi-tenant notes:** `doctor`/`validate` are **provider-agnostic** — they check for `AddAgentBlazor(`, `AddWorkflow<`, `MapAgentBlazorEndpoints(`, Mud providers, package refs, and target-framework shape only, and never inspect whether `UseOpenAI` or any `IChatClient` was registered. A proxy-wrapped `AddAgentBlazor(...)` passes cleanly; a clean PASS does **not** mean multitenancy wiring is correct. `analyze --scan-scope solution` filters tenant-resolution infrastructure (`*Provider`/infrastructure) as non-agent-relevant. See `references/multitenancy.md` for per-command notes and the full checklist.

Without a path argument the CLI auto-discovers a `.sln`/`.slnx`/`.csproj` in the current directory and prompts when several match (`-y` picks the first).

## Analyze an existing app (read-only)

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

- Writes `.agentblazor/analysis.md` next to the solution by default (`--output` to redirect). Never modifies application code.
- `--host` names the Blazor startup project used for host-shape and readiness checks; it does not restrict scanning.
- Default scan scope is the host project plus its project references. Use `--scan-scope solution` for multi-tenant/modular solutions where sibling projects are not referenced by the host project. Test projects are excluded.
- Skip the LLM call: `--static-only`.
- Windows/Roslyn MSBuildWorkspace failures fall back to static source-file analysis automatically; force it with `AGENTBLAZOR_STATIC_WORKSPACE=1`.

Provider configuration for LLM workflow suggestions: see `references/config-reference.md`.

## Scaffold baseline wiring

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --approve
```

- `--diff` shows exact file-level edits; `--approve` applies them. Prefer `--diff` first, then `--approve`.
- `--provider openai|azure-openai|ollama` registers provider wiring.
- `--use-local-source <PATH>` points at local AgentBlazor source projects instead of the package reference.
- Exits 2 when the plan is blocked (host is not Blazor, already installed, etc.).

After scaffold: `dotnet restore`, `dotnet build`, then `doctor` and `validate`.

## Onboard workflows (`scaffold workflows`)

See `references/workflow-onboarding.md` for the full flow: review artifacts, approval gating, SOUL/skill generation, and audit output.

## Multi-Tenant Solutions

The CLI handles **baseline wiring** for an existing multi-tenant solution. Runtime multitenancy (Finbuckle tenant resolution, proxy `IChatClient`, per-tenant conversation store, tenant-enrichment/cost-control middleware, circuit cookie) is **manual**, following the `ab-multitenancy` skill. See `references/multitenancy.md` for the full checklist + per-command notes + cross-reference map.

> This path is for *existing* `.sln`/`.slnx` Blazor solutions. If you are starting from scratch, do not begin here — `ab-multitenancy` + the AgentBlazor Starter sample are the right entry points; the CLI's manual-replacement dance is unnecessary for greenfield.

**Onboarding flow:**

1. `init` → `.agentblazor/AGENT.md` baseline
2. `analyze --scan-scope solution` → read the report **knowing tenant infrastructure is filtered out** (see per-command notes in `references/multitenancy.md`)
3. `scaffold --provider openai --diff` → preview the baseline wiring (`--provider` choice is a placeholder; it'll be replaced)
4. `scaffold --approve` → apply the *non-provider* wiring (imports, Mud services, endpoints, shell assets, chat surface)
5. **MANUAL** — in `Program.cs`, replace the generated `UseOpenAI` direct-provider block inside `AddAgentBlazor(...)` with the proxy `IChatClient` + `TenantAwareChatClient` per `ab-multitenancy` Step 6
6. **MANUAL** — Finbuckle wiring, per-tenant conversation store, tenant-enrichment/cost-control middleware, circuit cookie (per `ab-multitenancy` and the cross-reference map in `references/multitenancy.md`)
7. build → `doctor` (expect a clean PASS; it does **not** validate the proxy pattern) → manual verification of tenant isolation → `validate`
8. `update` / `watch` to keep `AGENT.md` current — re-running `scaffold` at any point is safe (idempotent for registration)

## Verify and keep current

- `doctor` → exit 0 when baseline wiring is complete; 2 when items are missing.
- `validate` → exit 0 when no blocking issues; 2 when blocking issues exist.
- `update` regenerates `.agentblazor/AGENT.md` when source changed (`--force` rebuilds unconditionally).
- `watch` keeps regenerating while you edit.
- Build integration: the package ships an MSBuild target that runs `agentblazor update --non-interactive --quiet` after build when `.agentblazor/AGENT.md` exists. Disable per-project with `<AgentBlazorAutoUpdate>false</AgentBlazorAutoUpdate>`.

## Safety rules

- `analyze` must only write the requested report — never application code.
- For multi-tenant solutions: `doctor`/`validate` are provider-agnostic and **cannot** validate proxy `IChatClient` or tenant-isolation patterns — a clean PASS means baseline wiring is present, not that multitenancy is correct. See `references/multitenancy.md`.
- Mutating suggestions are labeled with risk and require approval (`--approve`); suggestions referencing mutating methods carry `RequiresApproval` guidance.
- Workflow onboarding: reject suggestions not backed by static evidence, refuse writes outside the solution root, require separate approval per artifact type, and apply all writes through the audited agent-loop patch path (see `references/workflow-onboarding.md`).

## Config and environment variables

`agentblazor init --save-config` writes `.agentblazorc` (camelCase JSON, searched up the directory tree). Full option list and provider env vars: `references/config-reference.md`.

## Troubleshooting

- `No solution or project file found.` → pass an explicit path, or `-y` to pick the first match.
- `analyze` in an interactive terminal prompts for an OpenAI key when unset (never persisted). For CI set `OPENAI_API_KEY` and `AGENTBLAZOR_ANALYZE_MODEL`, or pass `--static-only`.
- MSBuildWorkspace load errors on Windows → static fallback is automatic; force with `AGENTBLAZOR_STATIC_WORKSPACE=1`.
- `AGENTBLAZOR_DEBUG=1` prints full exception details for doctor/validate failures.
- `update`/`watch` fail with exit 1 when `.agentblazor` was never initialized — run `init` first.
- **`scaffold` did not update my existing `AddAgentBlazor(...)` block** — scaffolding is append-only/non-destructive for the registration: the planner only plans the `agentblazor-services` patch when readiness reports `Missing`, and the applier only inserts when `AddAgentBlazor(` is absent. After you manually replace the scaffolded direct-provider block with a proxy `IChatClient` (for multi-tenant setups), re-running `scaffold` is inert — it neither re-adds nor overwrites your registration. To refresh an existing block, edit it by hand.
