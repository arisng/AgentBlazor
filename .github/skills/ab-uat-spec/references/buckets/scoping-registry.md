# UAT Bucket — Scoping & ResourceType Registry

> Aspect: caller-scoped conversation retrieval and the `ResourceType` registry validation.
> Shared prerequisites: see `../runbook.md` (§2 ports, §3 personas, §4 auth, §5 endpoint inventory, §6 wire contracts — ResourceType registry: `lifeline-session`, `lifeline`, `harvest`, `activity`, case-sensitive ordinal).
> **Prefix:** `SCOP-###` (e.g. `SCOP-001`) — bucket-local sequence.

Covers the `/active`, `/user/{userId}` (route-id-ignored), `/resource/{type}/{id}`, and
invalid-ResourceType-400 paths. Runs as Phase 10 (diagnostic, non-interactive) — placed
last as confirmation.

## SCOP-001 — /active returns caller-scoped conversations · Feature: scoping
- **GIVEN** Elena token (has ≥1 conversation)
- **WHEN** `GET /active`
- **THEN** every returned row has `UserId` = Elena's GUID; no other user's rows
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/SCOP-001-active.json`
- **Severity:** High

## SCOP-002 — /user/{userId} ignores the route userId · Feature: scoping
- **GIVEN** Elena token
- **WHEN** `GET /user/{jamesUserId}` (someone else's GUID in the route)
- **THEN** returns **Elena's** conversations (route param ignored; server-side caller scoping wins)
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/SCOP-002-user-route.json`
- **Severity:** High

## SCOP-003 — /resource/{resourceType}/{resourceId} returns matching rows for caller · Feature: scoping
- **GIVEN** Elena token + a `lifeline-session` conversation with known `ResourceId`
- **WHEN** `GET /resource/lifeline-session/{rid}`
- **THEN** only Elena's rows matching type+id are returned
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/SCOP-003-resource.json`
- **Severity:** High

## SCOP-004 — Invalid ResourceType → 400 · Feature: registry
- **GIVEN** Elena token
- **WHEN** `POST /{cid}/turns` with `ResourceType="Lifeline"` (capitalized) and again with `"bogus"`
- **THEN** HTTP **400** both times (registry is case-sensitive ordinal)
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/SCOP-004-400.json`
- **Severity:** High

## SCOP-005 — Cross-tenant isolation (API + browser) · Feature: scoping
- **GIVEN** David (meridian) token and Elena (vortex-labs) token
- **WHEN** compare `GET /active` for both; browser: David's `/sessions` + Chat tab
- **THEN** David sees only meridian conversations; Elena's vortex rows are absent from David's results (and vice versa); meridian DB rows never appear in vortex API responses
- **Tenant/User:** david.owusu / meridian, elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/SCOP-005-active-meridian.json`, `api/SCOP-005-active-vortex.json`, `ui/SCOP-005-meridian-browser.png`
- **Severity:** Blocker

## SCOP-006 — Stored UserId = caller (server-side), never from body · Feature: scoping
- **GIVEN** a conversation created as Elena (SESS-005)
- **WHEN** DB query on `AgentChatSessions.UserId` and response payload inspection
- **THEN** `UserId` == Elena's identity GUID (exists in `identity.Users`); append body carries no `UserId` key and any injected `UserId` field is ignored
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `db/SCOP-006-userid-row.sql`, `api/SCOP-006-response.json`
- **Severity:** High

## SCOP-007 — Session-tab conversation retrievable via /resource · Feature: registry/scoping
- **GIVEN** a conversation created via the SessionDetail Chat tab (`lifeline-session` + `ResourceId` = session GUID)
- **WHEN** `GET /resource/lifeline-session/{sessionId}`
- **THEN** the conversation is returned (retrievability path)
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/SCOP-007-resource.json`
- **Severity:** High
