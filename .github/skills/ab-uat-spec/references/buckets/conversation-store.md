# UAT Bucket — Conversation Store & Persistence

> Aspect: durable conversation persistence, the BFF-proxy fresh-scope rewrite path, and ResourceId data integrity.
> Shared prerequisites: see `../runbook.md` (§2 ports, §3 personas, §4 auth, §7 DB tables, §6 wire contracts).
> Remediation knowledge: `ab-conversation-store` (the capture → per-identity-cache → restore bridge
> and the singleton-proxy-over-scoped-store prerequisite) and `ab-entity-design` (session/turn entities,
> multitenancy columns, `BaseSessionId`, `AgentName`).
> **Prefix:** `PERS-###` (e.g. `PERS-001`) — bucket-local sequence.

Covers the **refresh-persistence** behavior (PERS-001), the **ResourceId data-integrity**
invariants (PERS-002, PERS-003), and the **fresh-scope rewrite** proxy path (PERS-004 —
log-line AND post-rewrite DB metadata). Runs as Phase 3 (PERS-001) and Phase 4 (PERS-002,
PERS-003, PERS-004).

## PERS-001 — Session-tab refresh persistence · Feature: ResourceId fix / BFF-proxy rewrite path
- **GIVEN** Elena opens a session page via the spec's `{sessionId}?tenant=vortex-labs` placeholder convention (**do NOT hard-code a GUID** — the spec is reusable), Chat tab, **New conversation**, sends a `P3UAT-` marker message, waits for the reply
- **WHEN** she **refreshes the page** and re-opens the Chat tab
- **THEN** the conversation is **STILL listed** in `AgentChatSessionBrowser`; the DB row has `ResourceType='lifeline-session'` + non-empty `ResourceId`; `GET /resource/lifeline-session/{sessionId}` returns it
- **Note:** this case directly exercises the BFF-proxy rewrite path; consumes one live turn from the runbook §10 budget (~6–8) — reuse the USG-001 conversation where possible
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/PERS-001-after-refresh-listed.png`, `db/PERS-001-row.sql`, `api/PERS-001-resource-query.json`
- **Severity:** **Blocker**

## PERS-002 — Data integrity: widget vs session-tab ResourceType/ResourceId · Feature: ResourceId fix
- **GIVEN** (a) a **widget-only** conversation created from the dashboard (global `AgentChatWidget`, no session page visit) and (b) a **session-tab** conversation created via the SessionDetail Chat tab, both as Elena
- **WHEN** `SELECT ResourceType, ResourceId, UserId, AgentName FROM [dprocess_tenant_vortex-labs].[agentchat].[AgentChatSessions] WHERE Content LIKE 'P3UAT-%'` (join `AgentChatTurns` to identify rows created this pass)
- **THEN** row (a) has `ResourceType = 'lifeline'` and `ResourceId = ''` (empty); row (b) has `ResourceType = 'lifeline-session'` and `ResourceId` = the session GUID (non-empty); no row created this pass has `ResourceType='lifeline-session'` with empty `ResourceId`
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `db/PERS-002-widget-row.sql`, `db/PERS-002-session-row.sql`
- **Severity:** **Blocker** (data-integrity exit criterion)

## PERS-003 — Regression guard: no stale ResourceId after navigating away · Feature: ResourceId fix
- **GIVEN** Elena visits a session page (sets the circuit-scoped `CurrentSessionId`) then navigates to the dashboard
- **WHEN** she sends a widget message from the dashboard
- **THEN** the new conversation row is `ResourceType='lifeline'` with empty `ResourceId` — **NOT** `lifeline-session` with a stale session GUID; additionally `GET /resource/lifeline-session/{visitedSessionId}` does **not** return this widget conversation (it is not mislabeled into the stale session scope)
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `db/PERS-003-widget-after-navigation.sql`, `api/PERS-003-resource-stale-session.json`
- **Severity:** **Blocker**

## PERS-004 — Fresh-scope rewrite preserves session resource context · Feature: proxy wiring (rewrite path)
- **GIVEN** a browser-created session-tab conversation on the SessionDetail Chat tab (Elena / vortex-labs, Development)
- **WHEN** a live turn completes (triggering the out-of-scope rewrite) and the page is refreshed
- **THEN** **BOTH** (AND): (a) Lifeline log shows `🔑 PROXY: Seeded fresh scope ILifelineSessionContext CurrentSessionId={SessionId}` (store `AgentChatConversationBffStore.cs` L182 — emitted at `LogInformation`, so logs must be captured at Information severity) AND (b) the DB row has `ResourceType='lifeline-session'` + non-empty `ResourceId`; the conversation is still listed after refresh
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `logs/PERS-004-fresh-scope-seed.log`, `db/PERS-004-row.sql`, `ui/PERS-004-after-refresh.png`
- **Severity:** **High** — direct regression for the D4 bridge firing; fails even if the DB is coincidentally correct
