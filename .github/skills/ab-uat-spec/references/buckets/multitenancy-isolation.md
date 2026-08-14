# UAT Bucket — Multitenancy & Cross-Tenant Isolation

> Aspect: tenant isolation (same-tenant, cross-tenant, root), API/DB-level access denial.
> Shared prerequisites: see `../runbook.md` (§2 ports, §3 tenant×user matrix + root-tenant blocker, §4 auth, §7 DB tables).
> Remediation knowledge: `ab-multitenancy` (Finbuckle tenant resolution, per-tenant stores).
> **Prefix:** `MTEN-###` (e.g. `MTEN-001`) — bucket-local sequence.

Covers the isolation guarantees that no tenant sees another's conversations, at the
browser, API, and DB layers. Runs as Phases 6–8, inheriting Phase 2 conversations.

## MTEN-001 — Same-tenant user isolation · Feature: multi-user isolation
- **GIVEN** vortex-labs/elena.kim has active conversations (created in Phase 2)
- **WHEN** john.smith (vortex-labs) logs in and navigates to the session page
- **THEN** AgentChatSessionBrowser shows "No conversations yet"
- **Note:** Uses `playwright-cli wait getByText('No conversations')` before snapshot. Clear browser state between personas. Tenant driven by `?tenant=vortex-labs` param.
- **Evidence:** `ui/MTEN-001-user-b-empty-list.png`, `ui/MTEN-001-page-url.png`
- **Severity:** Critical

## MTEN-002 — Cross-tenant isolation · Feature: cross-tenant isolation
- **GIVEN** vortex-labs/elena.kim has active conversations (created in Phase 2)
- **WHEN** david.owusu (meridian) logs in with `?tenant=meridian` and navigates to the session page
- **THEN** AgentChatSessionBrowser shows "No conversations yet"
- **Note:** `acme-corp` is NOT a seeded tenant — must use `meridian`. Same clear-state requirement as MTEN-001.
- **Evidence:** `ui/MTEN-002-meridian-empty.png`, `ui/MTEN-002-page-url.png`
- **Severity:** Critical

## MTEN-003 — Root tenant isolation · Feature: root tenant isolation
- **GIVEN** vortex-labs/elena.kim has active conversations
- **WHEN** admin@root.com logs in (root tenant) and navigates to any session page
- **THEN** AgentChatSessionBrowser shows "No conversations yet" for all tenants
- **Note:** Root admin manages tenant lifecycle only (profiles, subscription, infra) — has ZERO cross-tenant AgentChat data access. This confirms proper isolation, NOT a failure.
- **Evidence:** `ui/MTEN-003-root-empty.png`
- **Severity:** Critical

## MTEN-004 — API-level cross-tenant access denied · Feature: cross-tenant isolation
- **GIVEN** a conversation exists in vortex-labs with known ConversationId
- **WHEN** a curl request is sent to `GET /api/agentchat/resource/lifeline-session/{vortex-labs-session-id}` with john.smith's auth cookie (vortex-labs basic user)
- **THEN** HTTP response status is 403 or 404
- **Note:** Two-step: (1) `curl.exe -s -o nul -w "%{http_code}" -k -H "Cookie: {auth-cookie}" https://localhost:{port}/api/agentchat/resource/lifeline-session/{id}` for status code; (2) playwright-cli screenshot of browser navigating to the same URL as visual evidence. playwright-cli alone CANNOT assert HTTP status codes.
- **Evidence:** `api/MTEN-004-curl-output.txt`, `ui/MTEN-004-browser-response.png`
- **Severity:** Critical

## MTEN-005 — DB-level tenant isolation · Feature: cross-tenant isolation
- **GIVEN** conversations exist in vortex-labs (from Phase 2)
- **WHEN** querying `SELECT TenantId, COUNT(*) FROM ConversationSessions GROUP BY TenantId`
- **THEN** all rows show `vortex-labs` TenantId, zero rows for other tenants
- **Note:** Table is `ConversationSessions` (EF Core), NOT `AgentChatSessions`. Connection uses Aspire-discovered port. Run after Phase 2 creates rows so table schema exists.
- **Evidence:** `db/MTEN-005-tenant-grouping.sql`, `db/MTEN-005-tenant-grouping-result.txt`
- **Severity:** Critical

## MTEN-006 — Viewport + multi-user isolation · Feature: multi-user isolation
- **Same flow as MTEN-001 (same-tenant isolation)** but executed at 3 standard viewport sizes (default, tablet 768px, mobile 375px)
- Conversations must be EF Core-persisted (not InMemory) to survive viewport resets
- **Evidence:** `ui/MTEN-006-desktop.png`, `ui/MTEN-006-tablet.png`, `ui/MTEN-006-mobile.png`
- **Severity:** Major
