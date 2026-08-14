# git-fork-sync Evals

Structured test prompts for the git-fork-sync skill. For each prompt, the skill
should load and the response should match the described outcome. Run after
publishing and whenever models change.

## Positive cases (skill should fire and succeed)

### 1. Refresh a fork from upstream
**Prompt:** "Sync my fork from the upstream repository."
**Expected:** Claude runs `sync-upstream.ps1 -Mode Mirror` (or the equivalent
fetch + `--ff-only` sequence), refuses on a dirty tree, and does not rebase.

### 2. Pull upstream changes into the divergence branch
**Prompt:** "Pull the latest upstream master into my develop branch."
**Expected:** Claude uses the Integrate flow — refresh mirror, then merge into
`develop` with stash/restore of uncommitted work. No rebase of `develop`.

### 3. Reconcile local changes with upstream
**Prompt:** "I have uncommitted changes; sync me from upstream anyway."
**Expected:** Claude stashes with `-u`, performs the sync, restores the stash,
and reports where the stash is if anything fails.

### 4. Set up fork maintenance for a new repo
**Prompt:** "Create a gitops helper to keep this repo in sync with
https://github.com/owner/repo.git."
**Expected:** Claude produces a repo-agnostic script with the mirror/merge model
(no hardcoded repo-specific branches beyond overridable defaults) and documents
manual sync with `-DryRun` support.

## Negative cases (skill should not fire or gracefully abstain)

### 5. Unrelated branch management
**Prompt:** "Merge my feature branch into main."
**Expected:** The skill does not fire — no upstream synchronization involved.

### 6. Scheduled automation request
**Prompt:** "Add a GitHub Action that syncs from upstream every night."
**Expected:** Claude flags the divergence risk of auto-sync and asks whether the
user really wants it before adding any workflow (manual sync is the default).
