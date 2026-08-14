# UAT Bucket — Usage Pipeline

> Aspect: real-OpenAI usage pipeline — live turns, usage-record persistence, idempotency, cost aggregation.
> Shared prerequisites: see `../runbook.md` (§2 ports, §3 personas, §4 auth, §5 endpoint inventory #8/#9/#10, §6 wire contracts).
> **Cost note:** live OpenAI turns are budgeted ~6–8 per run (runbook §10/§11); USG-001 is the canonical live turn and doubles as runtime evidence for the bff-proxy bucket.
> **Prefix:** `USG-###` (e.g. `USG-001`) — bucket-local sequence.

Covers the E2E chat pipeline with a real key plus the `AgentUsageRecords` persistence,
per-conversation usage, aggregate summary, and idempotency. Runs as Phase 2 — creates the
conversation data subsequent isolation phases depend on.

## USG-001 — Live OpenAI chat turn via the global widget · Feature: usage E2E
- **GIVEN** Development stack with the real key; Elena logged in; `UseTenantStore=true`
- **WHEN** open the floating `AgentChatWidget` on the dashboard and send one message (content prefixed `P3UAT-`)
- **THEN** assistant reply renders (real `gpt-4o-mini`); no error toast; turn completes
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/USG-001-live-turn.png` (1920×1080), optional recording `ui/USG-001-live-turn.mp4`
- **Severity:** Blocker

## USG-002 — Usage row lands in AgentUsageRecords · Feature: usage
- **GIVEN** the USG-001 conversation (`{cid}` known)
- **WHEN** `SELECT * FROM [dprocess_tenant_vortex-labs].[agentchat].[AgentUsageRecords] WHERE ConversationId = '{cid}'`
- **THEN** ≥1 row: `ConversationId` matches, `TurnSequence` non-empty, `Model='gpt-4o-mini'`, `InputTokens>0`, `OutputTokens>0`
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `db/USG-002-usage-row.sql`
- **Severity:** High

## USG-003 — GET /{conversationId}/usage reflects tokens/cost · Feature: usage
- **GIVEN** the USG-002 row
- **WHEN** `GET {API}/api/v1/agent-chat/conversations/{cid}/usage`
- **THEN** returns the usage record(s); tokens > 0; matches the DB row
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/USG-003-usage.json`
- **Severity:** High

## USG-004 — GET /usage/summary reflects totals · Feature: usage
- **GIVEN** ≥1 usage row for Elena
- **WHEN** `GET {API}/api/v1/agent-chat/conversations/usage/summary?days=7`
- **THEN** `TotalRecords ≥ 1`, `DistinctConversations ≥ 1`, `TotalInputTokens > 0`, `TotalOutputTokens > 0`
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/USG-004-summary.json`
- **Severity:** High

## USG-005 — Duplicate (ConversationId, TurnSequence) → one row (idempotency) · Feature: usage
- **GIVEN** a successful `POST /{cid}/usage` with `(cid, turnSeq)`
- **WHEN** re-POST the identical `(cid, turnSeq)` payload
- **THEN** 2xx (no 409/500); DB has exactly **1** row for `(cid, turnSeq)` (unique index / ON CONFLICT DO NOTHING)
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/USG-005-dupe.json`, `db/USG-005-count.sql`
- **Severity:** High

## USG-006 — Failing usage POST is swallowed; turns unaffected · Feature: usage
- **GIVEN** Elena mid-chat
- **WHEN** a deliberately malformed `POST /{cid}/usage` (e.g., missing `ConversationId`) returns 400 **and** a subsequent chat turn is sent
- **THEN** the turn completes with a reply; no error toast; Lifeline logs show swallow-and-log of the failed POST
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `api/USG-006-bad-usage-400.json`, `ui/USG-006-next-turn.png`, `logs/USG-006-lifeline.log`
- **Severity:** Medium
