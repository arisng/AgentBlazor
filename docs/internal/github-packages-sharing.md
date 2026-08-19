# GitHub Packages — Private AgentBlazor NuGet Feed (Team-Shared)

---

## Relationship to other docs (adopted: OneDrive folder channel)

> **Decision (2026-08-19):** the **GitHub Packages** approach was evaluated and **set aside**
> in favor of a **shared OneDrive folder** as the distribution channel (see
> `docs/internal/github-packages-sharing.md` for the PAT/roles flow, kept for reference).
> Source repo stays a public fork (upstream PRs preserved); the `-internal.N` packages are
> distributed via the shared OneDrive folder and staged locally on each consumer machine.

- `docs/internal/github-packages-sharing.md` — the GitHub Packages flow (roles + PAT),
  documented for comparison/reference.
- `docs/internal/private-feed-publishing.md` — the machine-local `%USERPROFILE%\.agentblazor-feed`
  folder feed the consumer `nuget.config` points at.
- **OneDrive distribution channel:** a folder under the publisher's OneDrive personal
  library (e.g. `.\Nuget\AgentBlazor` relative to the OneDrive root; one `0.2.x-internal.N\`
  subfolder per version), shared manually to teammates. Teammates download the needed
  `.nupkg` files and copy them into their own `%USERPROFILE%\.agentblazor-feed\`. Never
  hardcode a `C:\Users\<user>\...` absolute path — resolve the OneDrive root per machine via
  `$env:OneDrive`.

---

Last updated: 2026-08-19 · Applies to fork builds (`*-internal.N`) that must stay off `nuget.org`.

This doc is the **authoritative** guide for sharing AgentBlazor fork builds with internal
team members through **GitHub Packages** (NuGet v3 feed). It replaces the machine-local
`%USERPROFILE%\.agentblazor-feed` workflow for team scenarios: teammates proactively pull
the latest version and install on demand, without access to this repo or a local file share.

Two roles:

| Role | Who | What they do |
|---|---|---|
| **Package author** | Maintainer of `github.com/arisng/AgentBlazor` | Publishes the `-internal.N` package set to GitHub Packages (from Actions or a PAT) |
| **Consuming developer** | Any internal team member | Authenticates, adds the feed, installs AgentBlazor at a pinned or floating version |

Feed URL (owner is the GitHub org/user that owns the repo; currently `arisng`):

```
https://nuget.pkg.github.com/arisng/index.json
```

> If publishing from a personal namespace, this owner stays the same as the repo owner.
> For an **org** namespace, package visibility and membership are managed at org level.

### Repo visibility posture (decision: public fork + private packages)

`arisng/AgentBlazor` is a **public fork** of `ashpeterson/AgentBlazor` and stays public so
upstream pull requests remain possible. Therefore:

- **Source is visible** — anyone can read the fork's internal changes.
- The **compiled `-internal.N` packages** are what must stay private. Publish them to GitHub
  Packages and **explicitly set each package to Private** with granular access roles.
- NuGet on GitHub Packages **requires authentication for every pull — even public packages** —
  so consumers always need a PAT + package read access.

**Critical caveat — package inheritance:** GitHub Packages NuGet supports granular
(user/org-scoped) permissions. If a published package becomes *linked* to this public repo,
it **inherits the repo's permissions by default** — and a public repo means everyone gets
read access. After each publish, remove the inherited permission and set Private visibility
(see [1.6 Set package visibility](#16-set-package-visibility-private--granular-access)).

> **Recommendation for team sharing:** `arisng` is a personal account, so package access
> grants are **per-user** (teams are only usable on org-scoped feeds). If the internal team
> grows beyond a few named individuals, create a **GitHub org** that owns the repo (or an
> org-scoped feed) and grant the package to an org **team**. See [Repo & feed scope recommendations](#repo--feed-scope-recommendations).

---

## Role 1 — Package Author

### 1.1 One-time repository setup

1. Ensure the repo has **GitHub Packages enabled** (Settings → Packages, or org-level
   "Inherit access from repository" policy). Packages are enabled by default.
2. **Package visibility is set per-package, not per-repo** (NUGet supports granular
   permissions). With a public fork, the plan is `-internal.N` packages → **Private**,
   public versions → **Public**. See [1.6](#16-set-package-visibility-private--granular-access).
3. Confirm the nuspec/`csproj` metadata is correct:
   - `PackageId` e.g. `AgentBlazor`
   - `Version` e.g. `0.2.24-internal.1`
   - `RepositoryUrl` **must equal** `https://github.com/arisng/AgentBlazor` so GitHub
     links the package to this repo (see the inheritance caveat below).
   - `RepositoryType` = `git`
   - `.nupkg` files are produced by `dotnet pack` at the exact version.

### 1.2 Choose an auth strategy for publishing

| Strategy | Token | When | Scope needed |
|---|---|---|---|
| **GitHub Actions** (CI publishing) | `GITHUB_TOKEN` (automatic per run) | Recommended daily/automated flow | `packages: write` (declared in workflow `permissions`) |
| **Manual publish from a dev machine** | Fine-grained PAT, or classic PAT | Occasional/one-off push outside CI | Packages: `Read and write` (classic: `write:packages`) |

**Prefer CI.** `GITHUB_TOKEN` is scoped to the run, auto-expiring, and needs no secrets
management on your machine.

### 1.3 Publish from GitHub Actions (recommended)

Create/replace the workflow shown in [`examples/publish-github-packages.yml`](examples/publish-github-packages.yml)
(or maintain a copy inline). Key points:

- `permissions: packages: write` is **required** — without it the push returns 401/403.
- The workflow packs the 8-project set and pushes each `.nupkg`/`.snupkg`.
- Push uses `--api-key "arisng:${{ secrets.GITHUB_TOKEN }}"` (owner:token form).
- `--skip-duplicate` makes re-running idempotent; a removed package can only be replaced
  with a **new version** (see Troubleshooting).

Trigger choices:

```yaml
on:
  workflow_dispatch:        # manual: enter version or re-publish
  push:
    tags: ['**-internal.*'] # auto on -internal tag push
```

### 1.4 Publish manually with a PAT (fallback)

```powershell
# 1) Add source (once)
dotnet nuget add source "https://nuget.pkg.github.com/arisng/index.json" `
  --name github-agentblazor-push `
  --username GITHUB_USERNAME `
  --password <PAT-with-write:packages> `
  --store-password-in-clear-text

# 2) Pack at version (e.g. 0.2.24-internal.1)
dotnet pack src/AgentBlazor.Components/AgentBlazor.Components.csproj -c Release -p:Version=0.2.24-internal.1

# 3) Push (api-key is <owner>:<token>)
dotnet nuget push "src/AgentBlazor.Components/bin/Release/AgentBlazor.0.2.24-internal.1.nupkg" `
  --source "https://nuget.pkg.github.com/arisng/index.json" `
  --api-key "arisng:<PAT>"
```

Repeat the push for each package in the set
(`AgentBlazor` from `AgentBlazor.Components`, `AgentBlazor.Core`, `AgentBlazor.Hosting`,
`AgentBlazor.Client`, `AgentBlazor.EntityFrameworkCore`, `AgentBlazor.ProviderAdapters`,
`AgentBlazor.Licensing`, plus the `AgentBlazor.Cli` tool).

### 1.5 After publishing

1. **Set each `-internal` package to Private + grant access** — see [1.6](#16-set-package-visibility-private--granular-access).
2. **Verify in the UI** — repo → Packages → each package listed with the new version and
   Private visibility.
3. **Verify as a consumer** (role 2 steps) using a *different* GitHub account or a
   read-scoped token — confirms the package is actually installable at the exact version.
4. **Tag** the release in git when useful (existing convention: `v0.2.24-internal.1`).

### 1.6 Set package visibility: Private + granular access

Because the repo is **public**, a package *linked* to it inherits public read access by
default. Do this for **each** `-internal.N` package after publish:

1. Open the repo → **Packages** tab → select the package (e.g. `AgentBlazor`).
2. Click **Package settings** (gear icon, right side).
3. **Remove inherited permissions:** under "Inherited access", deselect inheriting
   permissions from the repository. This is required before granular settings apply.
4. Set **Visibility → Private**.
5. Under **Manage access → Invite teams or people**, add each internal user (or an org team)
   with the **Read** role. For a personal-account feed, invites are **per-user**.
6. Repeat for every package that must stay internal.

NuGet consumers authenticate with a PAT regardless of visibility, so even a **Public**
package is not silently installable by anonymous `dotnet restore` — public here means
"any GitHub user with a `read:packages` token can fetch it", not "no auth required".

---

## Role 2 — Consuming Developer

### 2.1 Prerequisites

- A GitHub account that has been **granted Read access to the package** by the author
  (see [1.6](#16-set-package-visibility-private--granular-access)). Repo access alone is
  **not** enough for private packages — package-level access must be granted.
- A PAT with `read:packages` (or fine-grained Packages: `Read`).
  See [PAT Management](#pat-management).
- **NuGet always requires auth on GitHub Packages** — even a Public package is pulled
  with a `read:packages` token. No anonymous `dotnet restore` of GitHub-hosted packages.

### 2.2 Add the feed as a NuGet source (once per machine)

```powershell
dotnet nuget add source "https://nuget.pkg.github.com/arisng/index.json" `
  --name github-agentblazor `
  --username YOUR_GITHUB_USERNAME `
  --password <PAT-with-read:packages> `
  --store-password-in-clear-text
```

- On Windows, `--store-password-in-clear-text` writes the credential to the NuGet config;
  otherwise NuGet prompts or uses the credential store.
- Verify: `dotnet nuget list source` shows `github-agentblazor` enabled.

### 2.3 Install the latest version (on-demand pull)

```powershell
dotnet add package AgentBlazor --source github-agentblazor
```

NuGet picks the highest available version in that source. To be explicit:

```powershell
dotnet add package AgentBlazor --version 0.2.24-internal.1 --source github-agentblazor
```

For central package management repos, bump `Directory.Packages.props`:

```xml
<PackageVersion Include="AgentBlazor" Version="0.2.24-internal.1" />
```

### 2.4 Pin the source mapping (recommended)

If the consumer repo has `packageSourceMapping`, map `AgentBlazor*` to the GitHub source so
fork builds are never pulled from nuget.org:

```xml
<packageSources>
  <clear />
  <add key="github-agentblazor" value="https://nuget.pkg.github.com/arisng/index.json" />
  <add key="nuget" value="https://api.nuget.org/v3/index.json" />
</packageSources>
<packageSourceMapping>
  <packageSource key="github-agentblazor">
    <package pattern="AgentBlazor*" />
  </packageSource>
  <packageSource key="nuget">
    <package pattern="*" />
  </packageSource>
</packageSourceMapping>
```

### 2.5 Verify

```powershell
dotnet restore
# Check the resolved library in project.assets.json:
#   "AgentBlazor/0.2.24-internal.1" -> package
```

---

## Repo & feed scope recommendations

The current setup works but has a per-user ceiling. Plan the escape hatch in advance:

| Concern | Personal account (`arisng`) | GitHub org |
|---|---|---|
| Feed URL | `nuget.pkg.github.com/arisng/index.json` | `nuget.pkg.github.com/<org>/index.json` |
| Package access grants | Per-user only (invite each developer) | Org **teams** (add a member once, team inherits) |
| Team size | Small (≤ a few named devs) | Any size; onboarding/offboarding via team membership |
| Upstream PRs from the repo | Fine — repo stays public either way | Fine — repo stays public either way |
| Suggested migration | Start here today | Move when the team grows or cross-project consumers appear |

Migration path (when ready):

1. Create an org (or use an existing one); transfer/mirror `AgentBlazor` or create a new
   public org repo for the fork.
2. Re-run the publish flow against `https://nuget.pkg.github.com/<org>/index.json`.
3. Grant the internal **team** Read access on each private package once.
4. Update consumers' source URL + PAT scope (`read:packages`), same `nuget.config` shape.

---

## PAT Management

### 3.1 Classic vs fine-grained

| | Classic PAT | Fine-grained PAT |
|---|---|---|
| Scope wording | `read:packages`, `write:packages` | Packages: `Read` / `Read and write` |
| Scope target | All org/repo packages the token can see | Specific repositories only (context-narrow) |
| Expiry | Up to no expiry (not recommended) | Up to 1 year, per-token |
| Best for | Quick scripting, legacy flows | Team tokens, least-privilege by repo |

**Recommendation: use fine-grained PATs** scoped to `arisng/AgentBlazor`
(Packages: `Read` for consumers, `Read and write` for manual publication).

> Two independent conditions must both hold for a consumer to install a private package:
> 1. the user is **granted Read on the package** (author does this in Manage access), and
> 2. their PAT carries the **packages read** scope.

### 3.2 Scopes by role

| Role | Scope (fine-grained) | Scope (classic) |
|---|---|---|
| Package author (manual push) | AgentBlazor repo → Packages: **Read and write** | `write:packages` |
| Package author (CI) | none needed — `GITHUB_TOKEN` with `packages: write` in the workflow | — |
| Consuming developer | AgentBlazor repo → Packages: **Read** | `read:packages` |

### 3.3 Create a PAT

**Via the GitHub UI** — Settings → Developer settings → Personal access tokens →
Tokens (fine-grained): select `arisng/AgentBlazor`, pick Packages: `Read` or `Read and write`.

**Via CLI (fine-grained):**

```bash
gh auth login --scopes read:packages write:packages
# then reflect the quirk: gh CLI will provide the token; or create manually in UI.
```

(For classic: `gh auth token` cannot mint a classic token — create in the UI.)

### 3.4 Store & rotate

- **Do not** commit PATs to source control.
- Store inside the consuming repo as a **GitHub Actions secret** (`AGENTBLAZOR_GH_PAT`)
  for CI usage, or in the OS credential manager for local use.
- **Rotation** (e.g. quarterly): create a new PAT, update every consumer source with
  `dotnet nuget update source github-agentblazor --username ... --password <new> --store-password-in-clear-text`, then revoke the old token in the UI.
- **Revocation on incident**: revoke immediately in the UI; the feed becomes
  unauthenticated-push-fail / read-fail for that token within minutes.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `401 Unauthorized` / `Authentication failed` on push | `GITHUB_TOKEN` missing `packages: write`; wrong/missing PAT scope | Add `permissions: packages: write` to workflow job; issue PAT with `write:packages` |
| `403` on install | Token has no **package** read access (private package) or no repo read | Author must grant package Read role (Manage access); PAT needs `read:packages` |
| `404` package not found in source | Package version not yet published, or source credentials missing | Publish first; recheck `dotnet nuget list source`; confirm visibility |
| Version resolves from nuget.org instead of GitHub | No source mapping; package ID also on nuget.org | Add `packageSourceMapping` (`AgentBlazor*` → github source) |
| Push rejected "already exists" | GitHub Packages does not allow overwriting an existing version | Bump `-internal.N`; use `--skip-duplicate` in CI |
| `--api-key` errors on push | Wrong api-key format | Use `owner:token` form (`arisng:PAT` or `arisng:${{ secrets.GITHUB_TOKEN }}`) |
| Cache has stale version | NuGet cache / assets file | `dotnet nuget locals all --clear` (local) or delete `obj/project.assets.json` and restore |
