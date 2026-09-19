# Analysis Report Template

ALWAYS produce the report in this structure. Every finding row cites
`file:line` evidence. Severity uses the five levels from SKILL.md
(Critical / High / Medium / Low / Info).

## Template

```markdown
# Agent Registration & Authoring Audit — <app name>

**Audit date**: <date>
**Scope**: <path(s) audited>
**Method**: static analysis of registration surface + capability classes + registry seam; evidence-gated

## Executive summary

<One paragraph: how many agents audited (static vs store-seeded), how many
findings by severity, the single most important risk.>

| Metric | Count |
|---|---|
| Agents audited | N (static: a, store-seeded: b) |
| Findings | Critical: c · High: h · Medium: m · Low: l · Info: i |
| Not verified | k |

## Findings

| # | Severity | Aspect | Agent | File:Line | Issue | Evidence | Recommendation |
|---|----------|--------|-------|-----------|-------|----------|----------------|
| 1 | 🔴 Critical | Capability actions | Support Inbox Agent | Program.cs:512 | `AllowedCapabilityActions` references `support_inbox.draft_reply` but the capability exposes `draft_ticket_reply` | `[AgentAction(ActionId = "draft_ticket_reply")]` in SupportInboxCapabilities.cs:41 | Fix the allowlist id or the ActionId to match |
| 2 | 🟠 High | Data schemas | Support Inbox Agent | Program.cs:520 | `WithDataSchemas("support-data")` but no `AddDataSchema` named `support-data` | grep `AddDataSchema` → none match | Add the schema set or drop the reference |
| 3 | 🟡 Medium | Registry seam | — | Program.cs:180 | `IAsyncAgentRegistry` aliased to a different instance than `IAgentRegistry` | two `AddSingleton` factory lambdas | Alias both to the same singleton |
| … | | | | | | | |

## Per-agent detail

### <Agent name> — <source: static | store-seeded>

| Aspect | Value | Verdict |
|---|---|---|
| Name | … | ✅ / ⚠️ / ❌ / not verified |
| Description / Instructions | … | … |
| Route prefixes | … | … |
| Allowed components | … | … |
| Allowed actions | … | … |
| Allowed capability actions | … | … |
| Data schemas | … | … |
| Tools | … | … |
| Approvals | … | … |

<One or two sentences per agent summarizing its health and the top fix.>

### <Agent name> — …

## Cross-cutting findings

<Registry seam, provider config, middleware order, name-consistency contract —
any issue spanning more than one agent.>

## Prioritized fix list

1. **Critical** — <fix, owning skill>
2. **High** — …
3. …

## Appendix: evidence inventory

<Files and line ranges actually inspected, e.g. `Program.cs:300-560`, with the
grep patterns used to locate each surface.>
```

## Worked example (illustrative)

> ⚠️ **Hypothetical — not the live Demo state.** The findings below are
> illustrative examples of the report shape. The real Demo correctly aliases
> `IAgentRegistry` and `IAsyncAgentRegistry` to the **same**
> `DatabaseBackedAgentRegistry` singleton (Program.cs:213-224); do not report
> the seam finding as a real Demo defect.

```markdown
# Agent Registration & Authoring Audit — AgentBlazor.Demo

**Audit date**: 2026-09-19
**Scope**: demo/AgentBlazor.Demo (Program.cs, capabilities/, seeders)
**Method**: static analysis; evidence-gated

## Executive summary

Audited 10 agents (0 static, 10 store-seeded via DatabaseBackedAgentRegistry).
Found 1 High, 3 Medium, 2 Low. The dominant risk is that the seeder derives
capability-action ids by reflection while the runtime resolves them by the same
convention — any drift silently locks workflow agents out of their actions.

| Metric | Count |
|---|---|
| Agents audited | 10 (static: 0, store-seeded: 10) |
| Findings | Critical: 0 · High: 1 · Medium: 3 · Low: 2 · Info: 1 |
| Not verified | 0 |

## Findings

| # | Severity | Aspect | Agent | File:Line | Issue | Evidence | Recommendation |
|---|----------|--------|-------|-----------|-------|----------|----------------|
| 1 | 🟠 High | Registry seam | — | Program.cs:210-220 | Custom `IAgentRegistry` registered, but `IAsyncAgentRegistry` alias points at a different factory | two `AddSingleton` lambdas | Alias both interfaces to the same `DatabaseBackedAgentRegistry` singleton |
| 2 | 🟡 Medium | Capability actions | Supplier Compliance Agent | Program.cs:470 | Seeder `GetCapabilityActionIds` derives ids by reflection; any `ActionId` override must be mirrored | `[AgentAction(ActionId=…)]` + `ToSnakeCase` fallback | Keep `ActionId` overrides explicit or derive from the same attributes |
| … | | | | | | | |

## Per-agent detail

### Supplier Compliance Agent — store-seeded

| Aspect | Value | Verdict |
|---|---|---|
| Name | Supplier Compliance Agent | ✅ |
| Description / Instructions | present / shared default | ⚠️ shared instructions are generic |
| Route prefixes | /demo/workflows/supplier-compliance | ✅ |
| Allowed components | AgentDataGrid, AgentDialog | ✅ (ids exist in catalog) |
| Allowed capability actions | supplier_compliance.* | ⚠️ verify each against the capability class |
| … | | |

## Prioritized fix list

1. **High** — alias `IAsyncAgentRegistry` to the same instance (`ab-agent-registration`).
2. **Medium** — pin `ActionId` overrides in the seeder (`ab-capability-authoring`).
…

## Appendix: evidence inventory

- Program.cs:204-224 (registry seam — replace path + dual-interface alias), 430-560 (seeds), 560-643 (derivation helpers)
- capabilities/SupportInboxCapabilities.cs (actions, approvals)
- Greps: `AddAgent|AddWorkflow|AddCapability|AddDataSchema|AddTool|IAgentRegistry|AgentRegistration`
```

## Style rules

- Use `file:line` (or `file:line-range`) — never a bare file name.
- One row per finding; merge only identical root causes.
- Verdict glyphs: ✅ correct, ⚠️ issue (see finding #), ❌ broken, `not verified`.
- When a finding maps to an owning skill, name it in the Recommendation column.