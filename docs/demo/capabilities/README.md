# Capabilities & Actions

How agent capabilities, actions, approvals, and structured outputs work.

## Features in this category

| Guide | Feature | Status | ab\* skill |
|---|---|---|---|
| [Capability Authoring](capability-authoring.md) | `[AgentCapability]` + `[AgentAction]` | ✅ | `ab-capability-authoring` |
| [Approval Flows](approval-flows.md) | `RequiresApproval` boundaries | ✅ | `ab-capability-authoring`, `ab-in-chat-features` |
| [Clarification Requests](clarification-requests.md) | `NeedsClarification` pause-and-ask | ✅ | `ab-in-chat-features` |
| [Structured Outputs](structured-outputs.md) | `WithOutput`, `WithNextAction` | ✅ | `ab-capability-authoring` |
| [Recovery Playbooks](recovery-playbooks.md) | Error recovery + reset actions | ✅ | `ab-capability-authoring` |

## Not yet demoed

- `[AgentParam]` richer parameter contracts
- Handoff approval policy (`RequireHandoffApproval`)
