# Divergence from Upstream

This fork (`arisng/AgentBlazor`) is maintained from upstream
(`ashpeterson/AgentBlazor`) using the mirror/merge model:

- **`master`** — clean mirror of `upstream/master`. Never receives local commits.
  Sync manually with the fork-sync skill (`-Mode Mirror`).
- **`develop`** — this is where all divergence lives. Merge the refreshed
  mirror in periodically (`-Mode Integrate`).

Keep this list current: every deviation listed here is a permanent cost you pay
on every sync. The smaller the surface, the cheaper the merge.

## Committed divergence (lives on `develop`)

| Artifact | Why it exists | Sync impact |
|---|---|---|
| `docs/multi-tenant-production-blueprint.md` | Fork-specific design document for multi-tenant production scenarios | None (new file) |
| `.github/skills/git-fork-sync/` | Agent skill + script (`sync-upstream.ps1`) automating the manual fork-sync workflow | None (new files) |

## Local-only (never commit)

| File | Why | Note |
|---|---|---|
| `demo/AgentBlazor.Demo/appsettings.Development.json` | Contains a live OpenAI API key in `OpenAI:ApiKey` | **Never commit this file's local edits.** Stash it (`git stash push -- <file>`) before any sync, restore after. The key currently in the file has been exposed in a past session — rotate it in the OpenAI console and replace with a fresh value. |

## How to add a new divergence

1. Add a row to the table above with the file, reason, and sync impact.
2. Keep the change isolated in its own module/commit so upstream merges stay clean.
3. If the change is something upstream would accept, send it as a PR upstream
   instead — permanent reduction of future merge conflicts.
