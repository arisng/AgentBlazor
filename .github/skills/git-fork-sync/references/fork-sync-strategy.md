# Fork Sync Strategy

Load this file when the user asks *why* the mirror/merge model is used, wants merge-vs-rebase guidance, or asks how to minimize divergence cost over time.

## The core principle

Never develop on the branch that mirrors upstream. Separate "mirror" from "work":

- `master` — clean mirror of `upstream/master`. Only ever updated by sync. Never receives local commits.
- `develop` — your divergence line. Your work lives here.
- Short-lived feature branches → PR → `develop`.
- Periodically merge `master` into `develop` to stay current.

Once `master` contains both upstream *and* your changes, you lose fast-forward syncs, GitHub's "Sync fork" button stops working, and every sync becomes a conflict-resolution session.

## Merge vs rebase at each sync point

| Sync point | Recommendation | Why |
|---|---|---|
| upstream → mirror (`master`) | fast-forward merge | no divergence by construction → clean |
| mirror → dev (`develop`) | merge | conflicts resolved once, history preserved, no force-push |
| feature branch → `develop` | rebase / squash-merge | short-lived branches can be rewritten safely |
| `develop` → `origin` | normal push | it is shared — never rebase it |

Golden rule: **never rebase anything that has been pushed to a remote others can see.** Merge wins at the divergence point; rebase would give a linear history but at the cost of re-resolving the same conflicts forever and force-pushing.

## When rebase is acceptable

If the divergence is a small, permanent patch set ("patch queue") consumed only by the fork owner, a rebase flow with `--force-with-lease` is viable — linear history at the cost of re-resolving conflicts. Most teams with long-lived forks still pick merge.

## Keeping divergence cheap

1. **Document it** — a `DIVERGENCE.md` listing each deviation and why. The smaller the surface, the cheaper every sync.
2. **Contribute back** — send anything upstream would accept as a PR upstream. Permanent reduction of future conflicts.
3. **Isolate changes** — keep deviations in their own modules/commits, not interleaved with upstream files.
4. **Enable `rerere`** for recurring conflicts: `git config rerere.enabled true`.
5. **Pin releases** — build against upstream release tags when stability matters, not floating HEAD.
6. **Keep secrets out of the fork** — uncommitted local config (e.g. `appsettings.Development.json`) belongs in a gitignored local file, not committed to a fork.

## Sync cadence

Sync manually on your own schedule (upstream merged when *you* decide). If the user later asks for automation, the mirror model makes a "fetch upstream and open a sync PR" GitHub Action trivial — but never add scheduled automation unless requested.
