# Handoff: Consuming the local AgentBlazor NuGet packages from other projects

Date: 2026-08-18 · Repo: `c:\Workplace\DProcess\AgentBlazor` (branch `develop`)

## What this session should focus on

The next session should **instruct a user/fresh agent on how to consume the locally
published AgentBlazor NuGet packages from *other* projects** (consumer apps, starters,
samples, test harnesses) — i.e. wire up the private local feed as a package source and
`dotnet add package` the AgentBlazor packages at an exact version, without touching the
main repo.

The packages are **published to a private local feed** (never nuget.org). They are
`-internal.1` fork builds.

## State of the main repo (already done — do not redo)

The current released version is **`0.2.24-internal.1`** (chat markdown rendering
enhancement). As of this session it was:

- Version bumped in `Directory.Build.props` (`<Version>0.2.24-internal.1</Version>` +
  `PackageReleaseNotes`).
- Tagged (annotated, bare version) at commit `2a36cc5` (HEAD of `develop`):
  `git tag -a 0.2.24-internal.1`. Also tagged the prior release
  `0.2.23-internal.1` at commit `7f3a78a` (pre-markdown boundary).
- **Published to the private feed** via
  `scripts\publish-private-feed.ps1 -Pack -Version 0.2.24-internal.1`.

> Note: the two new tags are **bare + annotated**, which differs from the repo's other
> 17 release tags (all `v`-prefixed, lightweight: `v0.2.0`–`v0.2.21`). The commit `2a36cc5`
> and the two tags were **not pushed** to `origin` as of this handoff. Whether to re-tag as
> `v`-prefixed and/or push is an open decision for the user/next session.

## Where the canonical instructions live (do not duplicate)

The authoritative, maintained consumer instructions are already documented in
**`docs/internal/private-feed-publishing.md`** — read that file rather than re-authoring
the content here. Key sections: *Publishing* (script usage + `-DryRun`) and *Consuming*
(add source + add package). The publish script is **`scripts/publish-private-feed.ps1`**.

## How to consume the local packages from another project (quick reference)

These steps are for a **consumer project** (a separate app outside this repo).

1. **Add the feed as a NuGet source** (current-version flat mirror at the root):
   ```powershell
   dotnet nuget add source "%USERPROFILE%\.agentblazor-feed" --name agentblazor-local
   ```
2. **Add a package at the exact version**:
   ```powershell
   dotnet add package AgentBlazor --version 0.2.24-internal.1 --source agentblazor-local
   ```
   Other package IDs in the set: `AgentBlazor.Client`, `AgentBlazor.Core`,
   `AgentBlazor.EntityFrameworkCore`, `AgentBlazor.Hosting`,
   `AgentBlazor.ProviderAdapters`, `AgentBlazor.Licensing`, and CLI tool
   `AgentBlazor.Cli`.
3. **To pin an exact older version** (each version folder is itself a valid flat feed):
   ```powershell
   dotnet nuget add source "%USERPROFILE%\.agentblazor-feed\0.2.24-internal.1" --name agentblazor-0.2.24-internal.1
   dotnet add package AgentBlazor --version 0.2.24-internal.1 --source agentblazor-0.2.24-internal.1
   ```

### Feed layout (why the above works)

```
%USERPROFILE%\.agentblazor-feed\              <- root = flat mirror of CURRENT version (valid source)
    AgentBlazor.0.2.24-internal.1.nupkg
    ... (7 nupkg + 7 snupkg)
    └── 0.2.24-internal.1\                    <- per-version folder (organized archive + exact-version source)
        AgentBlazor.0.2.24-internal.1.nupkg
        ...
```

NuGet folder feeds only auto-discover nupkgs that are **flat** or in the **hierarchical
`<id>\<version>\`** layout. Version-first grouping (`<feed>\<version>\*.nupkg`) is **not**
auto-discovered at the root — that's why each version folder must be added as its own
source, and why the script keeps a flat mirror of the current version at the root.

### Environment variable

| Variable | Scope | Default |
|---|---|---|
| `AGENTBLAZOR_LOCAL_FEED` | User | `%USERPROFILE%\.agentblazor-feed` |

## Consumed package versions current at this handoff

- Current: `0.2.24-internal.1` (markdown rendering enhancement — sanitized Markdig
  pipeline, mermaid/nomnoml + highlight.js, code-copy buttons, `MarkdownOptions`).
- Prior: `0.2.23-internal.1` (`AgentBlazorRegistrationOptions.ConfigureChatOptions`
  provider hook, gpt-5.6-family `reasoning_effort` 400 fix).
- Full release notes: `docs/releases/0.2.23-internal.1.md` and
  `docs/releases/0.2.24-internal.1.md` (release-note docs are gitignored
  `[Rr]eleases/` — they are tracked via `git add -f`).

## Republishing notes (if a new version is needed)

- Bump then pack+publish in one step:
  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts\publish-private-feed.ps1 -Pack -Version <new-version>
  ```
- Idempotent: re-running the same version reproduces the same layout.
- `-DryRun -Verbose` previews without mutating the feed.

## Suggested skills for the next session

- **`mssql-cli`** — not needed here (no DB). Skip.
- **`git-atomic-commit`** / **`git-session-atomic-commits`** — if the consumer setup
  involves committing (e.g., documenting the consumer wiring or committing the final
  version-bump push), use these for atomic commits with conventional messages.
- **`ab-cli`** / **`ab-ui-integration`** — only relevant if the next session is also
  *onboarding* the package into a real consumer Blazor app via `agentblazor scaffold`
  / verifying static-asset loading; for pure package-version consumption they are
  not required.
- **`aspire-cli`** (via the aspire skills) — only if consuming inside an Aspire AppHost;
  not required for plain `dotnet add package`.

## Open decisions for the next session

1. Whether to **push** `develop` (`2a36cc5`) and the two tags to `origin`.
2. Whether to **re-tag** as `v`-prefixed + lightweight to match the 17 existing tags.
3. Whether the consumer should reference the **flat root** (tracks latest) or a
   **pinned version folder** (exact reproducibility) — depends on the consumer's needs.
