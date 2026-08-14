# Multi-Tenant Solutions — CLI Onboarding Notes

These notes describe how `agentblazor` CLI commands behave when onboarding an **existing multi-tenant** Blazor solution, and what the CLI cannot do. For the runtime multitenancy guide itself, see the **`ab-multitenancy`** skill (Finbuckle + proxy `IChatClient` + tenant-aware conversation store + middleware + circuit cookie).

> This path is for *existing* `.sln`/`.slnx` Blazor solutions. If you are starting from scratch, do not begin here — `ab-multitenancy` + the AgentBlazor Starter sample are the right entry points; the CLI's manual-replacement dance is unnecessary for greenfield.

## CLI does vs. manual (checklist)

| Concern | CLI can do | Manual (per `ab-multitenancy`) |
|---|---|---|
| Baseline `AddAgentBlazor(...)`, Mud services, endpoints, shell assets, chat surface | `scaffold --provider openai --diff` / `--approve` | — |
| `AGENT.md` baseline + keep current | `init` / `update` / `watch` | — |
| Analyze existing solution structure | `analyze --scan-scope solution` | — |
| Readiness/validation baseline checks | `doctor` / `validate` | — |
| Tenant resolution (Finbuckle) | — | Manual |
| Proxy `IChatClient` / `TenantAwareChatClient` | — | Replace scaffold's direct-provider block |
| Per-tenant conversation store | — | Manual (see `ab-conversation-store`) |
| Tenant enrichment & cost-control middleware | — | Manual (see `ab-middleware-authoring`) |
| Tenant-scoped agents & routes | — | Manual (see `ab-agent-registration`) |
| Per-tenant tool isolation | — | Manual (see `ab-tool-registration`) |
| Circuit cookie | — | Manual |

## Per-command multitenancy notes

*(all verified against `AgentBlazor.Cli` source — re-verify before relying on these if the CLI gains any multitenancy hook; see staleness footer)*

- **`analyze` / `analyze --scan-scope solution`** — scans every project. `AnalysisModelFilters` strips `*Provider` services and infrastructure, and `ServiceAnalyzer` drops classes ending in `Provider` — so **tenant-resolution infrastructure (Finbuckle stores/resolvers) is filtered out as "infrastructure," not surfaced as agent-relevant.** Route-quality notes may flag per-tenant multi-route hosts as low action-mapping. Expect to manually translate filtered tenant assets when reading the onboarding report.
- **`scaffold`** — generates a *single-direct-provider* wiring inside the `AddAgentBlazor(...)` lambda (`UseOpenAI` / `UseAzureOpenAI` / `UseOllama`). Replace that block with the proxy `IChatClient` + `TenantAwareChatClient` per `ab-multitenancy` Step 6. **Idempotent for registration**: the planner only plans the `agentblazor-services` patch when readiness reports `Missing`, and the applier only inserts when `AddAgentBlazor(` is absent — so re-running scaffold after the manual swap is safe and will not touch your registration.
- **`doctor` / `validate`** — **provider-agnostic.** They check for `AddAgentBlazor(`, `AddWorkflow<` / `.AddWorkflow(`, `MapAgentBlazorEndpoints(`, Mud providers, package refs, and target-framework shape only — there is *no* check that inspects whether `UseOpenAI` or any `IChatClient` was registered. A proxy-wrapped `AddAgentBlazor(...)` passes cleanly. Do **not** expect provider false positives; instead, know that these tools **cannot** validate the proxy/tenant pattern at all — their "PASS" does not mean multitenancy wiring is correct.
- **`scaffold workflows`** — discovers `[AgentCapability]` / workflow services across all projects (with `--scan-scope solution`), but does not scope them to tenants. Per-tenant agent/tool isolation and route binding must be reviewed/applied manually per `ab-agent-registration` + `ab-tool-registration`.

## Cross-reference map

| Multitenancy stack layer | Skill |
|---|---|
| Finbuckle tenant resolution, proxy `IChatClient`, `TenantAwareChatClient`, circuit cookie | `ab-multitenancy` |
| Per-tenant conversation store | `ab-conversation-store` (TenantConversationStore) |
| Tenant enrichment & cost-control middleware | `ab-middleware-authoring` (TenantCostControlMiddleware) |
| Tenant-scoped agents & route binding | `ab-agent-registration` |
| Per-tenant tool isolation | `ab-tool-registration` |
| `[AgentCapability]` / `[AgentAction]` shape (tenant-agnostic) | `ab-capability-authoring` |

## Onboarding flow (existing multi-tenant solution)

1. `init` → baseline `.agentblazor/AGENT.md`
2. `analyze --scan-scope solution` → read the report **knowing tenant infrastructure is filtered out**
3. `scaffold --provider openai --diff` → preview the baseline wiring (`--provider` choice is a placeholder; it'll be replaced)
4. `scaffold --approve` → apply the *non-provider* wiring (imports, Mud services, endpoints, shell assets, chat surface)
5. **MANUAL** — in `Program.cs`, replace the generated `UseOpenAI` direct-provider block inside `AddAgentBlazor(...)` with the proxy `IChatClient` + `TenantAwareChatClient` per `ab-multitenancy` Step 6
6. **MANUAL** — Finbuckle wiring, per-tenant conversation store, tenant-enrichment/cost-control middleware, circuit cookie (per `ab-multitenancy` and the cross-reference map above)
7. build → `doctor` (expect a clean PASS; it does **not** validate the proxy pattern) → manual verification of tenant isolation → `validate`
8. `update` / `watch` to keep `AGENT.md` current — re-running `scaffold` at any point is safe (idempotent for registration)

---

*Staleness footer*: Behavior documented as of `AgentBlazor.Cli` current source (2026-08). Re-verify `doctor`/`validate` checks and scaffold's `agentblazor-services` idempotency before treating these notes as current if the CLI gains any multitenancy hook.