# Agent Handoff & Handoff Approval — "switch control to another agent"

Handoff lets the conversation transfer control from one registered agent to another. It can require an in-chat approval card before the transfer happens.

## Enabling handoff

`EnableAgentHandoff` defaults to `true` on all three chat components. When multiple agents are registered, users (and the agent via command) can switch the active agent mid-conversation. Related toggles:

| Parameter | Default | Meaning |
|---|---|---|
| `EnableAgentHandoff` | `true` | Allow handoff at all |
| `RequireHandoffApproval` | `false` | Require user approval before any handoff |
| `HandoffApprovalPolicy` | `null` | Per-pair approval rules (see below) |
| `HandoffPolicy` | `null` | Additional pair/route rules evaluated for allow/deny |
| `MaxHandoffsPerSession` | `null` | Total handoffs allowed per session |
| `MaxHandoffsPerPair` | `null` | Lifetime handoffs for one (from → to) pair |
| `MaxHandoffsPerWindow` / `HandoffWindowMinutes` / `MaxPairHandoffsPerWindow` | `null` | Rolling-window rate limits |
| `BlockImmediateReturnHandoff` | `true` | Block A → B → A bounce-backs |
| `AutoNavigateOnRouteLockedHandoff` | `true` | Auto-navigate when a route-locked agent is targeted |
| `LockAgentToCurrentRoute` | `false` | Lock the active agent to the current route |

## The "Handoff Pending" card

When `RequireHandoffApproval = true` (or the per-pair policy says so), a transfer renders an in-chat card:

> **Handoff Pending**
> Transfer control from **A** to **B**? Route: `/...`
> [Approve Handoff] [Cancel]

Approve completes the transfer; Cancel keeps control with the current agent. The same actions are available as `/approve-handoff` and `/cancel-handoff` slash commands.

## `HandoffApprovalPolicy` — per-pair approval rules

Shape: `IReadOnlyDictionary<string, IReadOnlyList<string>>` mapping **source agent → target tokens**.

```csharp
HandoffApprovalPolicy = new Dictionary<string, IReadOnlyList<string>>
{
    ["support_inbox"] = new[] { "!refunds", "*" },   // support_inbox → everything except refunds needs approval
    ["*"] = new[] { "refunds", "admin_console" }     // wildcard: anyone → refunds/admin_console needs approval
}
```

Rule evaluation:

- A matching explicit source entry wins; otherwise a `"*"` wildcard entry; otherwise the `RequireHandoffApproval` default applies.
- Target tokens may use `!` to **exclude** a target from approval (and `!*` to deny all), and `"*"` to include all targets.
- Exact semantics: a listed target requires approval; a `!`-prefixed target never requires approval from that source.

`HandoffPolicy` uses the same shape but feeds allow/deny (violations block the transfer outright instead of asking), combined with the rate limits above. Run `/handoff-policy` in chat to see the effective summary: transitions, limits, window counts, approval rules, and pair rules.

## Slash commands for handoff

Typing `/` opens the command menu. Handoff-related items (only shown when `EnableAgentHandoff`):

| Command | Action |
|---|---|
| `/agents` | List registered agents |
| `/agent {name}` | Request handoff to that agent |
| `/handoff-history` | Show recent handoff transitions |
| `/handoff-policy` | Show the active handoff policy and limits |
| `/approve-handoff` | Approve a pending handoff request (when `RequireHandoffApproval`) |
| `/cancel-handoff` | Cancel a pending handoff request (when `RequireHandoffApproval`) |

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Handoff happens without asking | `RequireHandoffApproval` false and no matching `HandoffApprovalPolicy` rule |
| Card shows but agent already switched | The handoff card is pre-transfer; approve is what completes it — check who clicked/cancelled |
| `/agent X` ignored | Agent name mismatch, route-locked target without auto-navigation, or a handoff limit/violation (`/handoff-policy` explains) |
| Immediate bounce-back A→B→A blocked | `BlockImmediateReturnHandoff` (default true) — expected; relax it if A↔B collaboration is a real flow |
| No slash menu at all | `EnableAgentHandoff` false, or no agents registered (menu still lists component actions though) |
