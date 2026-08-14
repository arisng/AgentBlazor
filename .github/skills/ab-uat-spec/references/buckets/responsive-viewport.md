# UAT Bucket — Responsive Viewport

> Aspect: layout correctness of the session browser + chat surface + widget across viewport sizes.
> Shared prerequisites: see `../runbook.md` (§2 ports, §3 personas, §4 auth, §8 evidence viewport matrix: mobile 390×844 / tablet 768×1024 / desktop 1440×900).
> **Prefix:** `VPRT-###` (e.g. `VPRT-001`) — bucket-local sequence.

Covers responsive rendering only where layout matters. Runs as Phase 9, replaying Phase 2
(conversation view) and Phase 6 (empty state) flows.

## VPRT-001 — Mobile 390×844: session browser + widget usable · Feature: responsive
- **GIVEN** viewport 390×844
- **WHEN** open the SessionDetail Chat tab and the widget on the dashboard
- **THEN** no horizontal overflow; chat input usable; a widget message sends and replies
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/VPRT-001-mobile-session-browser.png`, `ui/VPRT-001-mobile-widget.png`
- **Severity:** Medium

## VPRT-002 — Tablet 768×1024: chat surface renders · Feature: responsive
- **GIVEN** viewport 768×1024
- **WHEN** open the SessionDetail Chat tab and the widget
- **THEN** surface + browser render without overlap or clipping
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/VPRT-002-tablet-chat.png`
- **Severity:** Medium

## VPRT-003 — Desktop 1440×900: full session-detail layout · Feature: responsive
- **GIVEN** viewport 1440×900
- **WHEN** open the SessionDetail Chat tab
- **THEN** session browser + `AgentChatSurface` layout correct; no overflow
- **Tenant/User:** elena.kim / vortex-labs · **Environment:** Development
- **Evidence:** `ui/VPRT-003-desktop-chat.png`
- **Severity:** Medium
