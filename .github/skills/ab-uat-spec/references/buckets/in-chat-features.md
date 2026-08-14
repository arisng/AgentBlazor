# UAT Bucket — In-Chat Features

> Aspect: interactive elements the agent renders inside the chat timeline (AgentBlazor).
> Status: **SCAFFOLD** — no test cases yet. Populate incrementally as `ab-in-chat-features` work lands.
> **Prefix:** `FEAT-###` (bucket-local sequence; scaffolds start at `FEAT-001` as cases land).
> Shared prerequisites: see `../runbook.md` once cases land (boot, personas, auth, evidence).

## Intended scope

Cases here assert that in-chat interaction features render and behave correctly in the
consumer app (Lifeline):

- **Approval flows** — `RequiresApproval` dialogs / `action.confirmation` generated-UI cards.
- **Clarification** — `NeedsClarification` pause-and-ask.
- **Handoff approval** — `RequireHandoffApproval` / `HandoffApprovalPolicy`.
- **Rich interactive output** — suggestion chips, proactive insights, next-actions, warnings,
  reasoning, execution-details expanders.
- **Slash commands**, agent selector, stop button, timeout warning, error boundary, dev tools
  (`ShowDevTools`).
- Component parameters that enable/tune each feature on `AgentChatSurface` / `AgentChatWidget` / `AgentChatPanel`.

## Example case shapes (draft — populate when the feature is exercised)

- Approval: an agent action marked `RequiresApproval` renders a confirm/deny dialog in the
  timeline; approving executes the action, denying aborts it (assert no side effect).
- Clarification: an action with `NeedsClarification` pauses for input and resumes with the
  collected value.
- Chips/next-actions: rendering a capability's `WithNextActions` produces tappable suggestion
  chips that trigger the follow-up action.

> Placeholder — replace with concrete GIVEN/WHEN/THEN cases as feature work lands.
