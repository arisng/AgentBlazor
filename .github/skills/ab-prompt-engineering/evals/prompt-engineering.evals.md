# ab-prompt-engineering Evals

Structured test prompts for the ab-prompt-engineering skill. For each prompt, the
skill should load and the response should match the described outcome. Run after
publishing and whenever models change.

These are consumer-side scenarios in an app that references the AgentBlazor NuGet
package and registers `[AgentCapability]` / `[AgentAction]` / `[AgentParam]` /
`[AgentComponent]` / `AddTool(...)` surface area.

## Positive cases (skill should fire and succeed)

### 1. Author a new system prompt
**Prompt:** "Write the system prompt for my Support Agent. It can `show_open_tickets`,
`explain_open_tickets`, and `draft_ticket_reply` (approval-gated)."
**Expected:** Claude runs the alignment workflow: inventories the registered actions,
drafts a prompt with a per-action "when to call" rule, marks `draft_ticket_reply` as
"propose and wait for approval", and does not claim any approval-gated action executes
immediately.

### 2. Align an existing prompt with registered capabilities
**Prompt:** "My Support Agent's prompt keeps suggesting actions it doesn't have. Make
its system prompt match what `WithAllowedActions` actually grants."
**Expected:** Claude regenerates the survey register, diffs the prompt against it, deletes
phantom action references, and closes gaps for registered but unmentioned actions.

### 3. Fix a hallucinated/ghost action
**Prompt:** "The agent keeps calling `close_ticket_immediately` but that action doesn't
exist. Fix the prompt."
**Expected:** Claude confirms the action ID is absent from registration (`[AgentAction]`
/ tool list), removes it from prose, and checks whether a sibling action should be named
instead.

### 4. Audit for approval-boundary drift
**Prompt:** "I flipped `RequiresApproval = true` on `submit_escalation_handoff`. Is my
prompt still aligned?"
**Expected:** Claude runs the drift audit, detects that the prompt does not tell the agent
to pause for user confirmation on that action, and proposes the prose fix — never editing
the action to match the stale prompt.

### 5. Survey the agent surface
**Prompt:** "Generate a machine-readable register of every capability, action, component,
and approval gate my agents expose so I can keep prompts in sync."
**Expected:** Claude runs `scripts/survey-agent-surface.ps1` and interprets the resulting
JSON as the baseline for prompt alignment.

### 6. Prompt section coverage review
**Prompt:** "Does my agent prompt cover schema usage, runtime-context, and generated-UI
outputs?"
**Expected:** Claude checks the draft against `references/prompt-content.md` sections
(schema usage, runtime context consumption, output shaping) and reports which sections are
missing or under-specified.

## Negative cases (skill should not fire or gracefully abstain)

### 7. Unrelated prompt question
**Prompt:** "How do I write a generic prompt for an unrelated API? Give me a template."
**Expected:** The skill does not fire — the prompt is not about an AgentBlazor agent's
registered capability surface.

### 8. Library internals question
**Prompt:** "Explain how AgentBlazor's `ChatClientRuntimeAdapter` builds prompts internally."
**Expected:** The skill does not fire (or abstains) — this is about package internals, not
authoring a consumer-visible system prompt; route to `ab-context-assembly` instead.

### 9. Tool-definition mechanics, not prose
**Prompt:** "How do I add a service tool and let a specific agent use it?"
**Expected:** The skill does not fire — this is a registration/setup question for
`ab-tool-registration`; alignment of prose is out of scope here.

### 10. Agent with no capabilities
**Prompt:** "My agent only has component access and service tools — no workflow capabilities. Write its system prompt."
**Expected:** Claude drafts a prompt focused on component actions and service tools, omitting the capability/action usage section entirely. The survey register has an empty `Capabilities` list but non-empty `Components` and/or `Tools`.

### 11. Multi-agent with different allowed surfaces
**Prompt:** "Agent A has `WithAllowedActions('support_inbox.show_open_tickets')` and Agent B has `WithAllowedActions('support_inbox.draft_ticket_reply')`. Make sure each agent's prompt only describes its allowed actions."
**Expected:** Claude produces two separate aligned prompts, each scoped to its agent's registration. The prompts do not describe actions outside each agent's `WithAllowedActions`.