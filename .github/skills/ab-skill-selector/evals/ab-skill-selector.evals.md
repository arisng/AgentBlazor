# ab-skill-selector Evals

Structured test prompts for the ab-skill-selector skill. For each prompt, the skill
should load (or not load) and the response should match the described outcome.
Run after publishing and whenever models change.

This skill's job is **selection and ordering**, not implementation: a passing
response names the skill(s), the order, and the handoff — then loads the owning
`SKILL.md` before acting.

## Positive cases (skill should fire and succeed)

### 1. Single-skill selection
**Prompt:** "gpt-5.6 keeps rejecting my function tools with HTTP 400
reasoning_effort — what do I do?"
**Expected:** Selects `ab-provider-config` only. States the single skill,
explains the `ConfigureChatOptions` pin, and does not pull in unrelated skills.

### 2. Multi-skill chain
**Prompt:** "Add an approval-gated `submit_escalation` action to my Support
Agent and make its prompt match what it can actually do."
**Expected:** Produces a chain — `ab-capability-authoring` →
`ab-agent-registration` → `ab-prompt-engineering` (plus
`ab-in-chat-features` for approval UX) — with that order justified (author →
register → align) and handoff notes per phase.

### 3. Disambiguation by dominant intent
**Prompt:** "My agent keeps calling `close_ticket_immediately`, which doesn't
exist. Fix it."
**Expected:** Selects `ab-prompt-engineering` (prompt alignment), NOT
`ab-capability-authoring` — the action should not be created just because the
prompt mentions it.

### 4. Which-skill question
**Prompt:** "Which skill should I use to persist conversations to SQL Server
and still browse them later?"
**Expected:** Chains `ab-entity-design` → `ab-conversation-store` →
`ab-chat-session-management`, naming all three and the order.

### 5. Multi-skill UI + persistence
**Prompt:** "Build a session browser so users can resume past chats."
**Expected:** Chain — `ab-chat-session-management` → `ab-conversation-store` →
`ab-mud-components` — data model before UI, with hydration entry points noted.

### 6. Multi-skill provider + tenant
**Prompt:** "We're going multi-tenant SaaS with per-tenant model options."
**Expected:** Chain — `ab-multitenancy` → `ab-provider-config` (plus
`ab-entity-design` / `ab-middleware-authoring` as needed for storage and tenant
enrichment), not a single-skill answer.

### 7. Debug selection
**Prompt:** "An agent run produced a bad answer — I need to see the prompt it
actually sent."
**Expected:** Selects `ab-inspector` first (prompt replay tab), then
`ab-context-assembly` if the assembled context needs changing.

### 8. Abstains correctly for missing skill
**Prompt:** "Use the `ab-other-components` skill to build my Radzen UI."
**Expected:** Flags that `ab-other-components` does not exist in
`.github/skills`, does not invent its content, and selects `ab-ui-integration`
+ `ab-mud-components` instead.

## Negative cases (skill should not fire or gracefully abstain)

### 9. Unrelated topic
**Prompt:** "Explain how to configure Serilog sinks in ASP.NET Core."
**Expected:** No AgentBlazor surface in the goal — the skill abstains and
answers directly (or the skill does not fire at all).

### 10. Generic Blazor question
**Prompt:** "How do I style a MudBlazor button in a plain Blazor app?"
**Expected:** No AgentBlazor surface — no selection; a general MudBlazor answer is
correct.

### 11. Single-skill goal already named by the user
**Prompt:** "Use `ab-cli` to run `doctor` on my solution."
**Expected:** The skill confirms the single-skill selection (fast path) and loads
`ab-cli`; it does not invent a chain or re-explain CLI internals itself.

### 12. No covering skill
**Prompt:** "How do I deploy AgentBlazor to AWS Lambda with a custom HTTP
adapter?"
**Expected:** No skill covers this — the skill says so explicitly rather than
stretching `ab-remote-chat` or `ab-multitenancy` to fit.