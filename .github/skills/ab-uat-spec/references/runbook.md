# AgentChat UAT Runbook — Shared Cross-Bucket Prerequisites

> Single source of truth for the **shared** preconditions every bucket depends on.
> Per-bucket test cases live in `buckets/<aspect>.md` and reference this runbook for
> boot, ports, tenants × users, auth flows, endpoint inventory, wire contracts, DB
> access, evidence conventions, execution order, exit criteria, and command templates.
> **Do not duplicate** shared content from this file into a bucket file — reference it.

## 1. Purpose & Scope

Full-regression UAT of every AgentChat feature across the hardening arc plus the
ResourceId data-integrity verification. Executed against the **Development-profile
Aspire local stack with a real OpenAI key**. Both **API-level assertions**
(curl / `Invoke-RestMethod`) and **browser flows** (Lifeline UI via playwright-cli)
are in scope. Negative tests are first-class cases (401 / 403 / 400 / idempotency /
no-op profile).

The UAT is organized into **focused aspect buckets** — see the routing table in
`SKILL.md`. Each bucket file lists its cases; the execution order (§10 below) sequences
them into a progressive user journey.

## 2. Environment (Development profile — Q3 locked)

### 2.1 Profile & stack
- **Profile:** `Development` only. No QA-region deployment in this pass.
- **Stack:** Aspire AppHost (local) · warm SQL Server container (persistent) · MinIO (auto-started by AppHost when env=Development) · real OpenAI key (`gpt-4o-mini`).

### 2.2 Port map (baseline — ALWAYS discover live ports, never hard-code)
| Service          | https | http        | Notes                                                            |
| ---------------- | ----- | ----------- | ---------------------------------------------------------------- |
| API              | 7030  | 5030        | REST + OTP debug endpoints                                       |
| Admin            | 7140  | 5032        | `/login` email+password, Admin role only                         |
| Lifeline         | 7142  | 5042        | `/auth/login`, `/auth/login/otp`                                 |
| Aspire Dashboard | 17273 | 15036       | resource view                                                    |
| SQL Server       | —     | 53935       | container `fsh-dev-sqlserver`, SA pwd `FSH_Test_SqlServer_2026!` |
| MinIO            | —     | 9000 / 9001 | bucket `dprocess-demo`                                           |
| OTLP             | —     | 4317        |                                                                  |

> **Critical:** An isolated run (worktree) serves dynamic ports (past evidence: Lifeline
> `https://localhost:51130`, API `https://localhost:51128`). Resolve every URL from the
> **running AppHost** via `aspire describe` / `aspire ps` — never assume the baseline ports.

### 2.3 Boot sequence (mandatory order)
1. **Warm SQL container** (must be up BEFORE the AppHost): `task aspire:database:start` (idempotent; port 53935; volume `dprocess-root-data`).
2. **Source secrets BEFORE boot** — the Dev profile does **NOT** auto-load `.secrets/development.env` (`Load-SecretEnv` skips Development); env vars set after AppHost start are ignored:
   ```powershell
   Get-Content .secrets/development.env | ForEach-Object {
     if ($_ -match '^([A-Z0-9_]+)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process') }
   }
   ```
   Verify (values redacted — never print the key): `$env:AgentChat__OpenAI__ApiKey` non-empty; `AgentChat:UseTenantStore` resolves `true` (appsettings.Development.json:21).
3. **Boot the stack**: standard checkout → `task aspire:run:dev`; **worktree → `task aspire:run:dev:isolated`** (port-conflict avoidance per repo guidance). Never raw `dotnet run`.
4. **Verify health**: `aspire ps` / dashboard — API, Admin, Lifeline in Running state; capture live URLs (`aspire describe`).
5. **Re-seed only if data was reset**: `task aspire:minio:start` → `task db:migrate:dev` → `task db:seed-demo` (or `task db:reset-demo` for full reset).

### 2.4 Config flags to confirm before testing
- `AgentChat:UseTenantStore=true` → BFF proxy DI path active (Program.cs:282–315).
- `identity.otp-authentication` enabled (default in appsettings.json).
- `AgentChat:OpenAI:ApiKey` resolves (config → `DPROCESS_OPENAI_API_KEY` process → user) — real key in `.secrets/development.env`.
- Model: `gpt-4o-mini`.

## 3. Tenants × Users matrix (Q3/Q4 locked)

**Canonical source:** `src/Playground/Playground.DbMigrator/DataSeeders/DemoTenants/qa-test-credentials.md` (code-accurate; supersedes runbook demo tables). All demo users share password `8-04^>P0$HnH` (`MultitenancyConstants.DefaultPassword`).

### 3.1 Matrix
| Tenant        | Persona           | Email                            | Role                       | Lifeline memberships                                            | UAT role in this spec                                                                                 |
| ------------- | ----------------- | -------------------------------- | -------------------------- | --------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `root`        | Root Admin        | `admin@root.com`                 | Admin                      | —                                                               | Root/admin flows (AUTH-006); ad-hoc AgentChat; dashboard shows empty states (no seeded business data) |
| `root`        | QA Agent (system) | `qa.agent@system.local`          | **Agent (Changelog-only)** | —                                                               | **API-only negative 403 cases (AUTH-002)** — CANNOT browser-test AgentChat/Lifeline (research-3)      |
| `vortex-labs` | Elena Kim (CTO)   | `elena.kim@vortex-labs.local`    | Admin                      | Project Nexus (Host), All Hands (Host)                          | **Primary browser/API persona** (SESS, USG, PERS, UI)                                                 |
| `vortex-labs` | Marcus Chen       | `marcus.chen@vortex-labs.local`  | Admin                      | Project Nexus, All Hands (CoHost)                               | Secondary admin (optional spot check)                                                                 |
| `vortex-labs` | James Wilson      | `james.wilson@vortex-labs.local` | Basic                      | All Hands                                                       | Basic-role chat (AUTH-003), Admin-app rejection (AUTH-005)                                            |
| `vortex-labs` | Sarah Connor      | `sarah.connor@vortex-labs.local` | Basic                      | *(none)*                                                        | Blank-slate (optional negative)                                                                       |
| `meridian`    | David Owusu       | `david.owusu@meridian.local`     | Admin                      | MedReg Compliance Review (Host), FinScale Implementation (Host) | Cross-tenant + click-to-resume (SESS-006, SCOP-005)                                                   |
| `meridian`    | Lisa Tanaka       | `lisa.tanaka@meridian.local`     | Basic                      | MedReg (CoHost)                                                 | Cross-tenant Basic (optional)                                                                         |
| `meridian`    | Michael Brown     | `michael.brown@meridian.local`   | Basic                      | *(none)*                                                        | Blank-slate (optional)                                                                                |

### 3.2 App access
| App      | Login path                                            | Auth                  | Who                                     |
| -------- | ----------------------------------------------------- | --------------------- | --------------------------------------- |
| Lifeline | `/auth/login` or `/auth/login/otp` (+ `?tenant=<id>`) | Email+password or OTP | Admin and Basic roles                   |
| Admin    | `/login`                                              | Email+password only   | Admin role only (Basic redirected back) |

### 3.3 Root-tenant blocker (research-3, locked)
`qa.agent@system.local` has the **Agent** role = Changelog-only → **403 on every Lifeline/AgentChat endpoint** (no `AgentChat.Conversations.*`, no `AgentChat.Usage.*`; permissions resolved server-side from role claims). **UAT workaround:** browser chat on demo tenants; root flows as `admin@root.com`; `qa.agent` used only as the API 403 probe. Root-tenant dashboard fix is **out of scope** — the UAT documents the blocker.

### 3.4 Seeded content (canonical)
- Vortex Labs: lifelines Project Nexus (Private), All Hands (Open); **6 sessions**; widget-created AgentChat rows may pre-exist for Elena (`ca57bce2…`, `fd6b9c25…` — legacy pre-fix data, see §12 assumptions).
- Meridian: lifelines MedReg Compliance Review (Private), FinScale Implementation (Hybrid); **3 sessions**.
- Content matrix (session topics, viewers, files): `qa-test-credentials.md` (canonical).

## 4. Auth flows

### 4.1 API-level (OTP — recommended)
Helper: `scripts/dev/Get-DevAuthToken.ps1 -Email <email> -Tenant <tenant> [-Environment Dev] [-Port <discovered-api-port>] [-OutputToken]`.
1. `POST /api/v1/identity/otp/request` (header `tenant: <id>`, body `{"email": "<email>"}`)
2. `GET /api/v1/identity/testing/otp-code?email=<email>` → `{"code":"123456"}` (debug endpoint, not rate-limited)
3. `POST /api/v1/identity/otp/verify` (`{"email","code"}`) → `{accessToken, refreshToken, tokenType:"Bearer", expiresIn:3600}`
- `/otp/request` is limited to 3/15min/email/tenant → a 429 means reuse an existing token or wait.

### 4.2 Browser (OTP)
`playwright-cli goto {LIFELINE}/auth/login/otp?tenant={tenant}` → fill `getByLabel('Email')` → `click "button:has-text('Send OTP')"` → retrieve code (4.1 step 2) → `fill "input[type='text']" {code}` → `click "button:has-text('Verify')"`.

### 4.3 Browser (email+password fallback)
`playwright-cli goto {LIFELINE}/auth/login?tenant={tenant}` → fill `getByLabel('Email')` + `getByLabel('Password')` (`8-04^>P0$HnH`) → `click "getByRole('button', { name: 'Sign in' })"`. Admin app: `{ADMIN}/login` → `click "getByRole('button', { name: 'Login' })"`.

## 5. AgentChat endpoint inventory (10 endpoints)

Group: `api/v1/agent-chat/conversations` (`AgentChatModule.cs:43`). **All 10 require a permission** (`.RequirePermission` — no `AllowAnonymous`).

| #   | Method + Path                               | Permission           | Notes                                                                     |
| --- | ------------------------------------------- | -------------------- | ------------------------------------------------------------------------- |
| 1   | `GET /active`                               | Conversations.View   | caller-scoped, recent turns only                                          |
| 2   | `GET /user/{userId}`                        | Conversations.View   | **route userId ignored — caller-scoped**                                  |
| 3   | `GET /resource/{resourceType}/{resourceId}` | Conversations.View   | caller-scoped + type/id filter                                            |
| 4   | `GET /{conversationId}`                     | Conversations.View   | session metadata                                                          |
| 5   | `POST /{conversationId}/turns`              | Conversations.Create | conversation created on first append; `ResourceType` validated (registry) |
| 6   | `DELETE /{conversationId}`                  | Conversations.Delete |                                                                           |
| 7   | `GET /{conversationId}/turns`               | Conversations.View   | turn timeline                                                             |
| 8   | `POST /{conversationId}/usage`              | Usage.Create         | idempotent on `(ConversationId, TurnSequence)`                            |
| 9   | `GET /{conversationId}/usage`               | Usage.View           | per-conversation usage                                                    |
| 10  | `GET /usage/summary?days={n}`               | Usage.View           | aggregate (use `?days=7`)                                                 |

## 6. Wire contracts (assertion targets)

- **AppendTurnRequest** (POST turns): `{ ResourceType, ResourceId, AgentName, Role, Content, Timestamp? }` — **NO `UserId`, NO `TenantId`** (both resolved server-side).
- **AppendTurnResponse**: `{ Id (Guid), ConversationId }`.
- **TurnItem**: `{ Sequence, Role, Content, Timestamp }`.
- **RecordUsageRequest** (POST usage): `{ ConversationId, TurnSequence (string), Model, InputTokens, OutputTokens, EstimatedCost?, Timestamp? }`; unique index `(ConversationId, TurnSequence)` → `ON CONFLICT DO NOTHING` semantics.
- **UsageSummaryResponse**: `{ TotalRecords, DistinctConversations, TotalInputTokens, TotalOutputTokens, TotalEstimatedCost }`.
- **Conversation response** includes `ConversationId`, `ResourceType`, `ResourceId`, `UserId` (server-set), `AgentName`, `CreatedAt`.

**ResourceType registry** (kebab-case, case-sensitive ordinal): `lifeline-session`, `lifeline`, `harvest`, `activity`. Invalid → 400.

## 7. DB access

- Connection: `localhost,53935`, SA user, pwd `FSH_Test_SqlServer_2026!` (container `fsh-dev-sqlserver`).
- Tenant DBs: `dprocess_tenant_vortex-labs`, `dprocess_tenant_meridian`; root DB name resolved at runtime (`SELECT name FROM sys.databases WHERE name LIKE '%root%'`).
- Key tables (schema `agentchat`): `AgentChatSessions` (`Id`, `ResourceType`, `ResourceId`, `ConversationId` nvarchar(512), `UserId`, `AgentName`, `CreatedAt`), `AgentChatTurns` (`Sequence`, `Role`, `Content`, `Timestamp`, unique `(SessionId, Sequence)`), `AgentUsageRecords` (`ConversationId`, `TurnSequence`, `Model`, `InputTokens`, `OutputTokens`, `EstimatedCost`, `Timestamp`, unique `(ConversationId, TurnSequence)`).
- **Row-scoping discipline:** DB assertions in the data-integrity bucket apply only to rows **created during this UAT** (identify via message marker prefix `P3UAT-` in `Content`). Legacy pre-fix rows (widget-origin `lifeline-session` + empty `ResourceId`) are reported separately (§11, open question).
- Query templates in §13 Appendix.

## 8. Evidence conventions

- **Evidence root:** the session-owned evidence directory (`{session-state}/files/<pass>/qa/evidence/<slug>/`) — session-owned; **never inside the worktree**. Location is resolved at runtime from the active session.
- **Layout:** `api/`, `ui/`, `db/`, `logs/` subfolders; file name = `{case-id}-{n}.{ext}` (e.g. `AUTH-001-401-matrix.json`, `COMP-001-widget-reply.png`).
- **Formats:** screenshots PNG (no devtools/chrome/overlays), default viewport **1920×1080** full-page; recordings MP4 H.264 only when specified; API evidence = raw captured HTTP response bodies + status codes (curl / `Invoke-RestMethod`); DB evidence = SQL query + result snapshot; log evidence = captured Lifeline/API log excerpts.
- **Viewport matrix (only for responsive cases):** mobile **390×844** / tablet **768×1024** / desktop **1440×900**.
- **playwright-cli only — never test scripts** (`open`/`goto`, `fill "getByLabel('Email')"`, `click "getByRole('button', { name: 'Sign in' })"`, `snapshot --filename=…`, `viewport` for responsive cases).
- **Required artifacts at evidence root:** `capture-manifest.json` (qa_context + per-capture metadata), `screenshots-audit.json` (per-case pass/fail), `defect-log.md` (ID, severity, case ref, symptom, remediation, status), `uat-verdict.md` (executor summary + verdict).

## 9. Test-data hygiene

- Prefix every turn created during a run with `P3UAT-` so DB assertions scope only to that run.
- **Never delete** other tenants' or other users' data.

## 10. Execution order — 11-phase progressive user journey

Run cases in this order; each block's preconditions come from the prior block. Phases build on data from earlier phases.

```
Phase 0:  ENV-001-005          (environment pre-flight)                     -> bucket: environment
Phase 1:  COMP-001, SESS-001   (first visit, empty state)                   -> buckets: chat-composer-ux, session-management
Phase 2:  USG-001-006          (first conversation — live turns, canonical) -> bucket: usage-pipeline
Phase 3:  SESS-002-006, SESS-007, PERS-001  (conversation management)       -> buckets: session-management, conversation-store
Phase 4:  PROX-001-006, PERS-004, PERS-002, PERS-003  (proxy wiring + DB probe) -> buckets: bff-proxy, conversation-store
Phase 5:  AUTH-001-006         (auth/permissions, single tenant)            -> bucket: auth-permissions
Phase 6:  MTEN-001             (same-tenant isolation)                      -> bucket: multitenancy-isolation
Phase 7:  MTEN-002, MTEN-004, MTEN-005   (cross-tenant isolation)           -> bucket: multitenancy-isolation
Phase 8:  MTEN-003             (root tenant isolation)                      -> bucket: multitenancy-isolation
Phase 9:  VPRT-001-003, MTEN-006 (viewport matrix; MTEN-006 = multi-user iso) -> buckets: responsive-viewport (VPRT-001-003), multitenancy-isolation (MTEN-006)
Phase 10: SCOP-001-007         (registry verification)                      -> bucket: scoping-registry
```

**Phase 0 — Environment pre-flight:** ENV-001 → ENV-005 (boot, discovery, auth pre-flight). Blocking — nothing runs without them.

**Phase 1 — First visit, empty state:** COMP-001 (global widget on dashboard, first message), SESS-001 (session browser lists conversations, but must create the first ad-hoc conversation via API first since no widget-origin ones exist yet). Creates the first conversation rows.

**Phase 2 — First conversation (canonical):** USG-001 → USG-006 (live OpenAI turns; **limit live turns** to ~6–8 total this pass to control key cost; USG-001 is the canonical live turn). This phase creates the conversation data that subsequent isolation phases depend on.

**Phase 3 — Conversation management:** SESS-002 → SESS-006 (click-to-resume, history loading, rehydration, cross-tenant resume); SESS-007 (browser-list staleness — new conversation stays reachable after switching); PERS-001 (session-tab refresh persistence — the BFF-proxy rewrite path). Reuses the conversation created in Phase 2.

**Phase 4 — Proxy wiring + DB probe:** PROX-001 → PROX-006 (proxy wiring — requires the live turn from USG-001 for runtime log evidence; **PERS-004** reuses that turn's browser-created session-tab conversation and asserts the fresh-scope log line + post-rewrite DB metadata); PERS-002 → PERS-003 (widget vs session-tab ResourceType/ResourceId data integrity).

**Phase 5 — Auth/permissions:** AUTH-001 → AUTH-006 (401/403/perm matrix; obtains the James/Elena/admin tokens used downstream). Single tenant — vortex-labs plus root.

**Phase 6 — Same-tenant isolation:** MTEN-001 (same-tenant user isolation). Requires Phase 2 conversations to exist in vortex-labs for Elena. John Smith (vortex-labs, Basic) must see "No conversations yet".

**Phase 7 — Cross-tenant isolation:** MTEN-002 (meridian user sees no vortex conversations), MTEN-004 (API-level cross-tenant access denied), MTEN-005 (DB-level tenant grouping check). All inherit Phase 2 conversations.

**Phase 8 — Root tenant isolation:** MTEN-003 (root admin sees no conversations for any tenant). Independent — no cross-tenant data inheritance.

**Phase 9 — Viewport matrix:** VPRT-001 → VPRT-003 (responsive viewport at 3 sizes), MTEN-006 (multi-user isolation across the same 3 viewports). Replays the flows from Phase 2 (conversation view) and Phase 6 (empty state).

**Phase 10 — Registry verification (diagnostic):** SCOP-001 → SCOP-007 (scoping + ResourceType registry validation). Non-interactive API checks, can run at any time but placed last as diagnostic confirmation.

**Close-out:** write `capture-manifest.json`, `screenshots-audit.json`, `defect-log.md`, `uat-verdict.md`; run the quality gate.

## 11. Assumptions & open questions (for the human conductor)

### Assumptions
1. **Root-tenant blocker stands (research-3).** `qa.agent@system.local` cannot browser-test AgentChat (Agent role → 403). UAT uses `admin@root.com` for root flows and demo tenants for chat; the blocker is documented, not fixed, in this pass.
2. **ResourceId fix is applied (finding Option A).** The data-integrity bucket asserts the **fixed** behavior (`ResourceType='lifeline'` + empty `ResourceId` for widget-origin conversations). If the BFF change is not yet merged, those cases fail **by design** and route to Backend Coder remediation (defect remediation is in scope).
3. **Legacy pre-fix rows.** Widget-origin rows created before the fix (`ca57bce2…`, `fd6b9c25…` — `lifeline-session` + empty `ResourceId`, user Elena) may still exist in the vortex DB. The data-integrity bucket asserts only on rows created **during this UAT** (marker `P3UAT-`). The legacy rows are reported as an informational finding.
4. **Dev profile does not auto-load secrets.** `.secrets/development.env` must be sourced into the process env before `task aspire:run:dev` (or `:isolated` in the worktree). QA performs this at boot; missing key = Blocker ENV-003.
5. **Ports are dynamic.** All URLs come from `aspire describe` on the running instance. Baseline 7030/7142 are reference only.
6. **Usage summary param is `?days=7`** (verified in `AgentChatModule.cs`; earlier plan text's `?from=&to=` shape is superseded by code).
7. **Live OpenAI turns are cost-bearing.** The spec caps live turns (~6–8) and reuses the USG-001 turn as runtime evidence for the bff-proxy bucket. **PERS-001 consumes one of the budgeted live turns** (it sends a browser Chat-tab message and waits for a real reply before refreshing) — reuse the USG-001 conversation where possible.

### Open questions
1. **Legacy-row normalization:** should pre-fix `lifeline-session` + empty-`ResourceId` rows be backfilled/normalized in this pass (migration), or accepted as known legacy data? Recommended: normalize (delete or relabel to `lifeline`) if any remain after the fix, else they violate the eventual "every `lifeline-session` row has non-empty `ResourceId`" invariant.
2. **Proxy runtime proof:** PROX-001 relies on Lifeline debug logs as runtime evidence that the BFF proxies (not Null*) were invoked. If debug logging is not observable in captured output, is code-level DI evidence + the live-turn no-exception check sufficient? (Recommended: yes, with an explicit note in the verdict.)
3. **Admin-app coverage:** only AUTH-005 touches the Admin app. Is full Admin regression in scope for this pass, or deferred? (Assumed deferred.)
4. **DB tooling:** DB queries assume `sqlcmd` or SSMS access to `localhost,53935` with SA credentials. If DB tooling is unavailable in the QA shell, confirm an alternative (e.g., a scripted `Invoke-Sqlcmd` via the AppHost) before execution.

## 12. Exit criteria

1. All 51 cases **pass** or are documented as known gaps — or blockers resolved (Backend Coder remediation) and **re-tested green** in the same run.
2. `defect-log.md` has **no open Blocker/High** severity entries.
3. Evidence complete: every case has its required artifacts; `capture-manifest.json` + `screenshots-audit.json` present and consistent (counts match).
4. Quality gate green: `task ax:quality:strict` passes (pre-close validation).
5. `uat-verdict.md` records per-case pass/fail and routes any residual Medium/Low defects with owner + disposition.
6. Handoff (PROX-006) committed reflecting the UAT outcome.

## 13. Appendix — reusable command/query templates

### OTP token (API)
```powershell
.\scripts\dev\Get-DevAuthToken.ps1 -Email elena.kim@vortex-labs.local -Tenant vortex-labs -Port <discovered-api-port> -OutputToken
```

### 401 matrix probe (example — repeat for all 10 endpoints)
```powershell
curl -k -o NUL -w "%{http_code}" https://localhost:7030/api/v1/agent-chat/conversations/active
```

### Append turn (API)
```powershell
$body = @{ ResourceType="lifeline"; ResourceId=""; AgentName="PM Assistant"; Role="user"; Content="P3UAT- smoke" } | ConvertTo-Json
curl -k -X POST "https://localhost:7030/api/v1/agent-chat/conversations/{cid}/turns" -H "Authorization: Bearer $token" -H "tenant: vortex-labs" -H "Content-Type: application/json" -d $body
```

### Usage summary (API)
```powershell
curl -k "https://localhost:7030/api/v1/agent-chat/conversations/usage/summary?days=7" -H "Authorization: Bearer $token" -H "tenant: vortex-labs"
```

### DB integrity check (data-integrity bucket scope)
```sql
SELECT s.Id, s.ResourceType, s.ResourceId, s.UserId, s.AgentName, t.Content
FROM [dprocess_tenant_vortex-labs].[agentchat].[AgentChatSessions] s
JOIN [dprocess_tenant_vortex-labs].[agentchat].[AgentChatTurns] t ON t.SessionId = s.Id
WHERE t.Content LIKE 'P3UAT-%'
ORDER BY s.CreatedAt;
```

### Usage row check (usage-pipeline bucket)
```sql
SELECT ConversationId, TurnSequence, Model, InputTokens, OutputTokens, EstimatedCost, Timestamp
FROM [dprocess_tenant_vortex-labs].[agentchat].[AgentUsageRecords]
WHERE ConversationId = '{cid}' ORDER BY Timestamp;
SELECT COUNT(*) AS DupeCount FROM [dprocess_tenant_vortex-labs].[agentchat].[AgentUsageRecords]
WHERE ConversationId = '{cid}' AND TurnSequence = '{turnSeq}';
```

### Legacy pre-fix rows (informational)
```sql
SELECT COUNT(*) AS LegacyMislabeled FROM [dprocess_tenant_vortex-labs].[agentchat].[AgentChatSessions]
WHERE ResourceType = 'lifeline-session' AND (ResourceId IS NULL OR ResourceId = '');
```
