# UAT Bucket — BFF Proxy Wiring

> Aspect: BFF proxy store DI wiring, registration ordering, failure swallow-and-log, store-disabled no-ops, handoff documentation.
> Shared prerequisites: see `../runbook.md` (§2.4 config flags, §3 personas, §4 auth, §7 DB tables).
> Note: the fresh-scope **rewrite path** case (PERS-004) is owned by the
> `conversation-store` bucket (it asserts DB + log-line persistence context, not wiring).
> **Prefix:** `PROX-###` (e.g. `PROX-001`) — bucket-local sequence.

Covers the DI-level proxy wiring behind `UseTenantStore=true`. Runs as Phase 4, requiring
the live turn from USG-001 for runtime log evidence.

## PROX-001 — DI resolves BFF proxies (not Null*) · Feature: proxy wiring
- **GIVEN** `UseTenantStore=true` on Development
- **WHEN** inspect `Playground.Lifeline/Program.cs` registrations (lines ~313–315) and capture runtime debug-log evidence during a live turn
- **THEN** `IActionHistoryStore` → `AgentChatActionHistoryBffStore` (Singleton); `IAuditLogService` → `AgentChatAuditBffService` (Scoped); `IUsageAnalyticsService` → `AgentChatUsageBffService` (Scoped); **none** resolve to `NullActionHistoryStore` / `NullAuditLogService` / `NullUsageAnalyticsService`; no TODO stubs referencing `/api/v1/agent-blazor/*` remain in the BFF services
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `logs/PROX-001-di-registration.txt` (code excerpt), `logs/PROX-001-runtime-proxy.log` (proxy debug lines during turn)
- **Severity:** High

## PROX-002 — Live turn exercises the wired paths without exceptions · Feature: proxy wiring
- **GIVEN** the proxies registered (PROX-001)
- **WHEN** a live turn completes through the widget/session chat (capability execution path that fires audit + action-history writes)
- **THEN** no exceptions; audit + action-history write paths invoked (log evidence); turn renders normally with no error toast
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/PROX-002-turn.png`, `logs/PROX-002-lifeline.log`
- **Severity:** High

## PROX-003 — Failures swallowed-and-logged (no turn breakage) · Feature: proxy wiring
- **GIVEN** BFF proxies wrap proxy calls in try/catch (code evidence) and the API is reachable
- **WHEN** a proxy-path failure occurs (e.g., a deliberately failed usage POST during a turn — see USG-006 — or one call to a temporarily unavailable endpoint)
- **THEN** the turn completes; failure is logged (warning/debug); no user-visible error; subsequent turns succeed
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `logs/PROX-003-failure.log`, `ui/PROX-003-turn-after-failure.png`
- **Severity:** High

## PROX-004 — Registration ordering: proxies before AddAgentBlazor · Feature: proxy wiring
- **GIVEN** `Program.cs` under the `useTenantStore` gate
- **WHEN** inspect registration order
- **THEN** proxies registered **before** `AddAgentBlazor`; `TryAddSingleton` cannot overwrite them; no duplicate registrations
- **Tenant/User:** n/a · **Environment:** Development (code evidence)
- **Evidence:** `logs/PROX-004-ordering.txt` (code excerpt)
- **Severity:** Medium

## PROX-005 — Store-disabled profile no-ops (UseTenantStore=false) · Feature: proxy wiring (negative)
- **GIVEN** the Testing profile (`AgentChat:UseTenantStore=false`)
- **WHEN** inspect `appsettings.Testing.json` + `AgentBlazorServiceCollectionExtensions` (`TryAddSingleton` Null* fallbacks)
- **THEN** Null* singletons are registered on that profile; `RecordAsync`/`LogAsync`/`GetSummaryAsync` no-op gracefully (no exceptions, no data written); a turn on that profile would not throw
- **Tenant/User:** n/a · **Environment:** Testing (config/code evidence; runtime check optional)
- **Evidence:** `logs/PROX-005-config.txt`
- **Severity:** Medium (informational guard)

## PROX-006 — Handoff reconciled · Feature: documentation
- **GIVEN** the main AgentChat handoff (`.agent-handoffs/260729-agent-chat-session-browser-resume.md`)
- **WHEN** read the file
- **THEN** it reflects the current hardening state + proxy wiring; no stale claims that click-to-resume is broken or auth/usage are "next steps"
- **Tenant/User:** n/a · **Environment:** n/a (repo evidence)
- **Evidence:** `logs/PROX-006-handoff-excerpt.txt`
- **Severity:** Medium
