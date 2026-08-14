# UAT Bucket — Auth & Permissions

> Aspect: 401/403 matrix, role gates, Basic/Admin/root access controls.
> Shared prerequisites: see `../runbook.md` (§2.2 ports, §3 tenant×user matrix, §4 auth flows, §5 endpoint inventory, §6 wire contracts).
> Remediation knowledge: permission model lives in the repo's `permission-system` skill and `Identity`/`Multitenancy` modules; the root-tenant 403 blocker is documented in runbook §3.3.
> **Prefix:** `AUTH-###` (e.g. `AUTH-001`) — bucket-local sequence.

Auth is the **gate** every API/browser flow passes through. Run as Phase 5 (single tenant
— vortex-labs plus root). Obtains the James/Elena/admin tokens used downstream.

## AUTH-001 — 401 on all 10 AgentChat endpoints unauthenticated · Feature: auth
- **GIVEN** no `Authorization` header
- **WHEN** each of the 10 endpoints (runbook §5) is called (use a throwaway `{cid}`, `{uid}`, `{rid}`)
- **THEN** HTTP **401** on all 10 (no `AllowAnonymous` remains)
- **Tenant/User:** anonymous · **Environment:** Development
- **Evidence:** `api/AUTH-001-401-matrix.json` (10 status codes + bodies)
- **Severity:** Blocker

## AUTH-002 — 403 without pinned permission (root Agent role) · Feature: auth
- **GIVEN** OTP token for `qa.agent@system.local` (root, **Agent** role = Changelog-only)
- **WHEN** `GET /active`, `GET /user/{uid}`, `GET /resource/lifeline/{rid}`, `POST /{cid}/turns`, `POST /{cid}/usage`
- **THEN** HTTP **403** on each (no `AgentChat.Conversations.*` / `AgentChat.Usage.*`)
- **Tenant/User:** qa.agent@system.local / root · **Environment:** Development
- **Evidence:** `api/AUTH-002-403-matrix.json`
- **Severity:** High

## AUTH-003 — Basic-role user chat works (IsBasic:true grants) · Feature: auth
- **GIVEN** OTP token for James (vortex-labs, Basic — carries `IsBasic:true` AgentChat+Usage perms)
- **WHEN** `POST /{cid}/turns` (new conversation) then `GET /{cid}/turns`
- **THEN** 2xx on append; turns readable; Basic role satisfies `Conversations.Create`/`View`
- **Tenant/User:** james.wilson / vortex-labs · **Environment:** Development
- **Evidence:** `api/AUTH-003-append.json`, `api/AUTH-003-turns.json`
- **Severity:** High

## AUTH-004 — Admin-role user chat works · Feature: auth
- **GIVEN** OTP token for Elena (vortex-labs, Admin)
- **WHEN** append + read turns on a new conversation
- **THEN** 2xx; works identically to Basic
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/AUTH-004-append.json`
- **Severity:** Medium

## AUTH-005 — Admin app rejects Basic user · Feature: auth (app access)
- **GIVEN** Admin app running
- **WHEN** `{ADMIN}/login` as James (Basic)
- **THEN** redirected back to login; no session; then login as Elena (Admin) succeeds → dashboard
- **Tenant/User:** james.wilson, elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/AUTH-005-admin-basic-rejected.png`, `ui/AUTH-005-admin-success.png`
- **Severity:** Medium

## AUTH-006 — Root admin can create an ad-hoc AgentChat conversation · Feature: auth (root)
- **GIVEN** OTP token for `admin@root.com` (root, Admin role — has AgentChat perms per research-3)
- **WHEN** `POST /{cid}/turns` with `ResourceType=lifeline`, `ResourceId=""`
- **THEN** 2xx; conversation created in the root tenant DB (root has no seeded sessions — ad-hoc is expected)
- **Tenant/User:** admin@root.com / root · **Environment:** Development
- **Evidence:** `api/AUTH-006-append.json`, `db/AUTH-006-root-row.sql`
- **Severity:** Medium
