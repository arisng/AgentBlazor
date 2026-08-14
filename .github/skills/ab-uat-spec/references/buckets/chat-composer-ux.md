# UAT Bucket — Chat Composer & UX

> Aspect: chat surfaces (widget), composer input behavior, send/reply round-trips on a consumer app.
> Shared prerequisites: see `../runbook.md` (§2 ports, §3 personas, §4 auth, §7 DB tables).
> Remediation knowledge: `ab-chat-composer` (composer script loading + keyboard behavior)
> and `ab-mud-components` (chat surfaces category).
> **Prefix:** `COMP-###` (e.g. `COMP-001`) — bucket-local sequence.

This bucket covers the **consumer-side surface behavior** of AgentBlazor chat on the
Lifeline app — how the float/embedded widget is wired, how sending a message produces a
reply, and how history survives reopen. Fill additional composer-specific cases
(script loading, Enter-to-send, text retention) in as those features are worked.

## COMP-001 — Global widget on a non-session page (dashboard) · Feature: ResourceId fix / widget
- **GIVEN** Elena logged in on the dashboard (non-session page)
- **WHEN** open the floating `AgentChatWidget`; send a message (`P3UAT-` prefix); wait for the assistant reply; close and reopen the widget
- **THEN** the reply renders; the conversation **persists** (history visible on reopen); DB row exists with `ResourceType='lifeline'` and empty `ResourceId`
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/COMP-001-widget-open.png`, `ui/COMP-001-widget-reply.png`, `ui/COMP-001-widget-reopen-history.png`, `db/COMP-001-widget-row.sql`
- **Severity:** **Blocker**

> **Scaffold notes (incremental fill):** composer-specific cases (typed-text retention on
> Send, Enter-to-send vs Shift+Enter newline, missing `AgentBlazor.min.js` script tag
> diagnostic) belong here when that work is done — see `ab-chat-composer` for the
> diagnostic patterns and `ab-mud-components` for surface variants
> (`AgentChatSurface` / `AgentChatWidget` / `AgentChatPanel`).
