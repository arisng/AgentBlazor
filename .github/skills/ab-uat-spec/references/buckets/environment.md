# UAT Bucket — Environment

> Aspect: stack boot, port discovery, tenants × users, auth pre-flight.
> Shared prerequisites: see `../runbook.md` (§2 boot/ports, §3 tenants×users, §4 auth flows).
> **Per-case format:** ID · Feature · GIVEN/WHEN/THEN · Tenant/User · Environment · Evidence · Severity/Exit.
> Severity scale: **Blocker** (must pass to close pass) · **High** (feature regression) · **Medium** (guard/observability) · **Low** (informational).
> Placeholders: `{API}` `{LIFELINE}` `{ADMIN}` = live URLs from `aspire describe`; `{cid}` = conversation id.

> **Prefix:** `ENV-###` (e.g. `ENV-001`) — bucket-local sequence.

The environment bucket is the **blocking precondition** for every other bucket — nothing
runs without these green (Phase 0). Re-read `runbook.md` §2 (boot sequence) and §4 (auth
flows) before starting.

## ENV-001 — Warm SQL container
- **GIVEN** UAT is about to start on the Development profile
- **WHEN** `docker ps --filter name=fsh-dev-sqlserver` and a TCP probe to `localhost:53935`
- **THEN** container `Running`; port listening; `task aspire:database:start` is idempotent
- **Tenant/User:** n/a · **Environment:** Development
- **Evidence:** `db/ENV-001-docker-ps.txt`, port probe result
- **Severity:** Blocker

## ENV-002 — AppHost up, live endpoints discovered
- **GIVEN** the stack booted per runbook §2.3
- **WHEN** `aspire ps` / `aspire describe`
- **THEN** API, Admin, Lifeline are Running; live https URLs captured (`{API}` `{LIFELINE}` `{ADMIN}`) — baseline 7030/7140/7142 or isolated dynamic ports
- **Tenant/User:** n/a · **Environment:** Development
- **Evidence:** `logs/ENV-002-aspire-describe.txt`
- **Severity:** Blocker

## ENV-003 — Real OpenAI key resolvable
- **GIVEN** `.secrets/development.env` sourced into the process env before boot (runbook §2.3)
- **WHEN** env-var presence check (value redacted) and one live chat turn (validated in USG-001)
- **THEN** `AgentChat__OpenAI__ApiKey` non-empty and the live turn completes with an assistant reply
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `logs/ENV-003-key-presence.txt` (no value), live-turn evidence via USG-001
- **Severity:** Blocker

## ENV-004 — UseTenantStore=true (proxy DI path active)
- **GIVEN** Development profile appsettings
- **WHEN** inspect `appsettings.Development.json` + `Program.cs` gating
- **THEN** `AgentChat:UseTenantStore=true`; proxy registrations present (Program.cs:313-315)
- **Tenant/User:** n/a · **Environment:** Development
- **Evidence:** `logs/ENV-004-config.txt` (redacted excerpt)
- **Severity:** Blocker

## ENV-005 — OTP auth pre-flight (API + browser)
- **GIVEN** API and Lifeline running
- **WHEN** `scripts/dev/Get-DevAuthToken.ps1 -Email james.wilson@vortex-labs.local -Tenant vortex-labs -Port <discovered>` and a browser OTP login (runbook §4.2)
- **THEN** `accessToken` returned (expiresIn 3600); browser lands on the dashboard without an error banner
- **Tenant/User:** james.wilson / vortex-labs · **Environment:** Development
- **Evidence:** `api/ENV-005-token.json` (token redacted), `ui/ENV-005-otp-login.png`
- **Severity:** Blocker
