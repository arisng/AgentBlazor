# Drift Audit — Detecting When the Prompt Falls Out of Sync

A repeatable, cheap procedure for checking whether an AgentBlazor agent's system prompt still matches its **registered** surface after additive changes (new capability, `[AgentAction]`, `[AgentComponent]`, tool, approval gate, or data schema). Run it whenever surface area changes, and before shipping a prompt revision.

## Trigger — when to audit

Run the drift audit when any of these happen:

- A capability gained/lost an `[AgentAction]`, changed a `[AgentParam]`, or flipped `RequiresApproval`.
- A new `[AgentComponent]` was added or an existing one's action/readable set changed.
- A service/MCP tool was added (`AddTool`/`UseMcpServer`) or an agent's `WithAllowedActions` changed.
- An agent's `WithDataSchemas(...)` changed.
- You refactored the prompt and want to confirm it did not silently drop coverage.

## Inputs

- The current `WithInstructions(string)` text.
- The current survey register (e.g. `agent-surface.json`) — regenerate it first:

```powershell
powershell -File scripts/survey-agent-surface.ps1 -SourceDir ./Components -OutFile agent-surface.json
```

- The previous register/prompt pair you committed in [Phase 5](../alignment-workflow.md).

## Audit steps

1. **Regenerate the register** against current source. This is the source of truth.
2. **Diff the register** (old vs new) to list what changed — added/removed/changed action IDs, approval flags, params, components, tools, schemas.
3. **Filter the diff** down to changes that affect prompt prose:
   - Added action/component/tool/schema → does the prompt now mention it? If not, **gap**.
   - Removed action/component/tool → does the prompt still mention it? If yes, **phantom**.
   - `RequiresApproval` flipped true → prompt must now say "propose and wait"; flipped false → prompt must not say so.
   - `[AgentParam]` added/`Required` flipped → prompt must name it / mark it required.
4. **Classify findings**:
   - **Critical** — prompt instructs something the runtime will reject (phantom action ID, claims "approve it yourself" on an approval-gated action, describes an action that no longer exists). Fix before shipping.
   - **Warning** — prompt under-describes a newly added surface (gap); likely to be under-used. Fix proactively.
   - **Info** — cosmetic mismatch in wording that does not change behavior.
5. **Produce a fix list** mapping each finding to the prose change in the relevant [`prompt-content.md`](prompt-content.md) section.

## Deliverable

A short report:

- Line or so summarizing the change set (the register diff).
- A table of findings: `Type (Critical/Warning/Info)` | `Old state` | `New state` | `Prose fix needed`.
- The updated register committed alongside the corrected prompt.

## Guardrails

- **Never "fix" the drift by editing the action.** If prose and registration disagree, the registration is the source of truth; change the prose.
- **Do not over-edit.** An `Info` wording mismatch may be left alone if it does not affect behavior; flag it rather than rewrite wholesale.
- **Confirm with a probe turn** after applying fixes (Phase 4 in the alignment workflow) — a drifted prompt is best caught by inspection, not reason alone.