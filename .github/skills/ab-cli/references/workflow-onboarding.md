# Workflow Onboarding (`scaffold workflows`)

The V2 onboarding path that proposes multi-step agent workflows for an existing app and generates AgentBlazor-owned SOUL/skill artifacts. Manual `[AgentCapability]` / `[AgentAction]` authoring stays valid; this command adds an approved, auditable pipeline on top.

## When to use

- The user wants workflows, SOUL.md, or skill files generated for an app instead of manually authoring capability/action attributes.
- The user needs review artifacts for team approval of proposed agent workflows.
- Baseline wiring already exists (or will be applied separately; baseline wiring and workflow artifacts are separate approval groups).

## Command form

```bash
agentblazor scaffold workflows ./MySolution.slnx --host MyBlazorApp --provider openai --diff
agentblazor scaffold workflows ./MySolution.slnx --host MyBlazorApp --provider openai --approve
```

Positional arguments: first argument is `workflows`, second is the solution/project path.

## Analysis inputs

The command builds a corpus from the scanned projects:

- routes and pages
- services, public methods, and DI registrations
- existing `[AgentCapability]` / `[AgentAction]` workflows
- multi-method process clusters (lifecycle, route-correlated, domain-correlated)
- domain terms and file references

## Options

| Option | Purpose |
|---|---|
| `--description <TEXT>` | Short app description used in onboarding intent |
| `--agent-goals <GOALS>` | Comma- or semicolon-separated workflows the app agent should help users accomplish |
| `--save-config` | Save onboarding intent to `.agentblazorc` |
| `--scan-scope <references\|solution>` | Projects to scan (default `references`) |
| `--workflow <IDS>` | Candidate ids/slugs to apply in non-interactive mode |
| `--reject <IDS>` | Mark candidates rejected in the review artifact |
| `--pin <IDS>` / `--unpin <IDS>` | Pin/unpin candidates in the review artifact |
| `--apply-approved` | Apply workflows already marked approved in `.agentblazor/workflow-onboarding.json` |
| `--reviewed-by <NAME>` | Reviewer identity recorded in review artifacts and audit |
| `--approve` | Apply the approved workflow artifacts |
| `--diff` | Show proposed file diffs |
| `-y\|--non-interactive` | Skip prompts; in non-interactive mode without `--workflow`, write a report instead of applying |

## Review artifacts (deterministic, under `.agentblazor/`)

- `workflow-onboarding.json` — machine-readable candidate decisions
- `workflow-onboarding.md` — markdown review artifact
- `workflow-onboarding.html` — browsable review artifact

Write them first, review decisions, then apply.

## Approval flow

1. Generate/propose candidates (`--diff`, or non-interactive report).
2. Decide per candidate: `--workflow <ids> --approve`, `--reject <ids>`, `--pin <ids>`, `--unpin <ids>`; record `--reviewed-by <name>`.
3. Apply approved workflows (`--approve`, or `--apply-approved` to re-apply decisions already recorded in the JSON).

## Generated artifacts for approved workflows

- `.agentblazor/SOUL.md` — project restrictions and agent charter
- `.agentblazor/skills/index.json`
- `.agentblazor/skills/<skill-slug>/SKILL.md` + optional reference files
- `.agentblazor/skills/.metadata.json`
- `.agentblazor/audit/workflow-onboarding-<timestamp>.json` — reviewer, workflow decision metadata, proposed/applied files, tool transcript

Skill progressive disclosure APIs exist in the analysis layer (index, full SKILL.md, reference views). Skills track reads/executions and curator metadata; pinned skills are preserved, unpinned skills go stale after 30 inactive days and are archived after another 60.

## Safety rules

- Reject workflow suggestions that reference methods not present in static evidence.
- Reject direct writes outside the detected solution root unless explicitly approved.
- Require separate approvals for: analysis artifacts, SOUL.md, skill files, capability/workflow classes, Program.cs/service wiring, UI/chat wiring, and validation commands.
- Encode restrictions at three levels: SOUL.md project restrictions, CLI session mode/file-scope restrictions, and SKILL.md frontmatter restrictions.
- The model must never write files directly — all writes go through the AgentBlazor-owned agent-loop patch proposal → preview → approved-apply pipeline with an auditable tool transcript.

## Verification

- Unit tests cover workflow cluster evidence, retrieval, unsupported-suggestion rejection, SOUL/SKILL determinism, skill view restrictions, and stale/archive behavior.
- CLI verification covers read-only analyze, scaffold diff preview, approved artifact generation, non-interactive refusal for ambiguous workflow application, and unchanged baseline scaffold behavior.
