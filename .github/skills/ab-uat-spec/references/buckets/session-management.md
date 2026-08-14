# UAT Bucket — Session Management

> Aspect: browse/resume/hydrate past conversations, conversation identity, browser-list freshness.
> Shared prerequisites: see `../runbook.md` (§2 ports, §3 personas, §4 auth, §7 DB tables, §6 wire contracts).
> Remediation knowledge: `ab-chat-session-management` (session browser, `GetActiveSessionsAsync`,
> `HydrateTimelineFromHistoryAsync`, refresh-persistence visibility invariant).
> **Prefix:** `SESS-###` (e.g. `SESS-001`) — bucket-local sequence.

Covers click-to-resume and the browser `AgentChatSessionBrowser` list — including the
browser-list-staleness regression (SESS-007). Runs as Phase 1 (SESS-001) and Phase 3
(SESS-002..006, SESS-007), inheriting Phase 2 conversations.

## SESS-001 — Session browser lists 2+ conversations · Feature: click-to-resume
- **GIVEN** Elena (vortex-labs) has ≥2 conversations (pre-existing widget rows `ca57bce2…`/`fd6b9c25…`, or create 2 ad-hoc via API with `P3UAT-` marker content)
- **WHEN** open `{LIFELINE}/sessions/{sessionId}?tenant=vortex-labs` → **Chat** tab
- **THEN** `AgentChatSessionBrowser` lists ≥2 conversation entries (non-empty, each with a title/snippet)
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/SESS-001-session-browser.png` (1920×1080)
- **Severity:** High

## SESS-002 — Click a past conversation loads history · Feature: click-to-resume
- **GIVEN** the SESS-001 list rendered
- **WHEN** click a past conversation
- **THEN** `AgentChatSurface` renders the turn timeline; turns ≥ 1; content non-blank; no error banner
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/SESS-002-history-rendered.png`
- **Severity:** High

## SESS-003 — New → click past conversation → rehydration (negative path) · Feature: click-to-resume
- **GIVEN** the chat surface open on a conversation
- **WHEN** click **New** (empty surface, 0 turns) then click the past conversation again
- **THEN** timeline hydrates from empty to full history; no PK violation / turn duplication; browser console 0 errors
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/SESS-003-empty.png`, `ui/SESS-003-rehydrated.png`
- **Severity:** High

## SESS-004 — ConversationId is an opaque full GUID · Feature: conversation identity
- **GIVEN** a conversation created via the wire contract (SESS-005)
- **WHEN** `GET {API}/api/v1/agent-chat/conversations/{cid}/turns` and DB query
- **THEN** `ConversationId` is a full opaque GUID in N format (32 hex chars), **no `::agent::` suffix**, no tenant prefix; matches DB `AgentChatSessions.ConversationId` (nvarchar(512))
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/SESS-004-get-turns.json`, `db/SESS-004-session-row.sql`
- **Severity:** High

## SESS-005 — ResourceType/ResourceId persisted; no UserId in request body · Feature: conversation identity
- **GIVEN** the append-turn wire contract (runbook §6) — body has **no** `UserId`/`TenantId`
- **WHEN** `POST {API}/api/v1/agent-chat/conversations/{cid}/turns` as Elena with `ResourceType=lifeline-session`, `ResourceId=<valid session GUID>`, `AgentName`, `Role`, `Content`
- **THEN** 200/201; `AgentChatSessions` row persisted with `ResourceType` + `ResourceId`; stored `UserId` = Elena's identity GUID (server-resolved); captured request body contains **no** `UserId` key
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/SESS-005-append-request.json` (captured body), `db/SESS-005-session-row.sql`
- **Severity:** High

## SESS-006 — Click-to-resume on Meridian (cross-tenant) · Feature: click-to-resume
- **GIVEN** David (meridian) has ≥2 conversations (create 2 ad-hoc via API as David if none)
- **WHEN** open a Meridian session Chat tab and click a past conversation
- **THEN** history loads non-blank; conversation rows live in `dprocess_tenant_meridian` (DB check)
- **Tenant/User:** david.owusu / meridian · **Environment:** Development
- **Evidence:** `ui/SESS-006-meridian-resume.png`, `db/SESS-006-meridian-row.sql`
- **Severity:** Medium

## SESS-007 — New conversation stays reachable after switching (browser-list staleness) · Feature: click-to-resume / browser list
- **GIVEN** Elena opens a session page via the spec's `{sessionId}?tenant=vortex-labs` placeholder convention, Chat tab, and the browser already lists **≥1** pre-existing conversation (call it chat-session-1)
- **WHEN** she clicks **New** (chat-session-2 appears as an empty surface), sends a `P3UAT-` marker message, waits for the reply, then clicks **chat-session-1** in `AgentChatSessionBrowser` so it hydrates
- **THEN** `AgentChatSessionBrowser` **still lists both** chat-session-1 **and** chat-session-2; clicking chat-session-2 re-hydrates its timeline (the `P3UAT-` marker turn is visible) — the new conversation is never unreachable
- **Note:** this case closes the browser-list coverage gap: `SessionDetailViewModel.CreateNewChatSession` previously never added the new conversation to the in-memory `ChatSessions`, so switching away hid it (no way back). Fixed via placeholder-on-create + `IAgentChatSessionEvents.SessionUpdated` in-place refresh (`UpsertChatSessionSummary`). Reuses the same session resource as PERS-001; no extra live turn required if chat-session-2's message is sent once. Remediation knowledge: `ab-chat-session-management` (refresh-persistence / browser-list visibility invariant)
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/SESS-007-new-listed.png`, `ui/SESS-007-after-switch-both-listed.png`, `ui/SESS-007-back-to-new-hydrated.png`
- **Severity:** **Blocker**
