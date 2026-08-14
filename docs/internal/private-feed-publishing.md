# Private Feed Publishing (Per-Version Folder Layout)

Last updated: 2026-08-15

How AgentBlazor fork builds are published to the private local NuGet feed with an enforced per-version folder layout, and how consumers install from it.

## Layout

```
%USERPROFILE%\.agentblazor-feed\          ← feed root (flat mirror of the CURRENT version — always a valid source)
│   AgentBlazor.0.2.23-internal.1.nupkg
│   AgentBlazor.Hosting.0.2.23-internal.1.nupkg
│   ...
│
└── 0.2.23-internal.1\                     ← per-version folder (organized archive + exact-version source)
        AgentBlazor.0.2.23-internal.1.nupkg
        AgentBlazor.0.2.23-internal.1.snupkg
        ...
```

- **Feed root** keeps a flat mirror of the **current** version only. This is the stable primary source.
- **Version folders** (`<feed>\<version>\`) hold every `.nupkg`/`.snupkg` of that version. Each folder is itself a valid flat NuGet source and doubles as the archive.

## Why version folders are not auto-discovered at the root

NuGet folder feeds auto-discover nupkgs in two layouts only:

1. **Flat** — `*.nupkg` directly in the folder.
2. **Hierarchical** — `<feed>\<packageId>\<version>\<packageId>.<version>.nupkg` (grouped by **package ID**, then version).

Version-first grouping (`<feed>\<version>\*.nupkg`) is **not** auto-discovered. That is why the publish script keeps the current version flat at the root (so `--source <feed>` keeps working) while also staging the same files into the version folder (so the archive is organized and each version is independently consumable).

## Publishing

Use the enforced script — it packs, organizes, archives, and mirrors in one step:

```powershell
# Uses <Version> from Directory.Build.props (e.g. 0.2.23-internal.1)
powershell -ExecutionPolicy Bypass -File scripts\publish-private-feed.ps1 -Pack

# Explicit version, or reuse already-packed bin\Release nupkgs without packing
powershell -ExecutionPolicy Bypass -File scripts\publish-private-feed.ps1 -Pack -Version 0.2.23-internal.1
powershell -ExecutionPolicy Bypass -File scripts\publish-private-feed.ps1 -Version 0.2.23-internal.1

# Preview without mutating the feed
powershell -ExecutionPolicy Bypass -File scripts\publish-private-feed.ps1 -DryRun -Verbose
```

What it does:

1. Resolves the version (`-Version`, else `<Version>` in `Directory.Build.props`) and the feed root (`AGENTBLAZOR_LOCAL_FEED` user env var, else `%USERPROFILE%\.agentblazor-feed`).
2. Packs the 8-package set (Licensing, Core, ProviderAdapters, Hosting, Components, Client, EntityFrameworkCore, Cli) at that version — or reuses existing `bin\Release` nupkgs when `-Pack` is omitted.
3. Creates `<feed>\<version>\` and copies every `.nupkg`/`.snupkg` there (organized archive).
4. Archives any older flat nupkgs found at the root into their own version folders (keeps the root tidy).
5. Copies the current version's files to the root (flat mirror — keeps the root a valid source).

The script is idempotent: re-running the same version reproduces the same layout.

## Consuming

```powershell
# Primary source (current version)
dotnet nuget add source "%USERPROFILE%\.agentblazor-feed" --name agentblazor-local

dotnet add package AgentBlazor --version 0.2.23-internal.1 --source agentblazor-local
```

To pin an exact older version, add that version folder as a source (it is a valid flat feed):

```powershell
dotnet nuget add source "%USERPROFILE%\.agentblazor-feed\0.2.23-internal.1" --name agentblazor-0.2.23-internal.1
dotnet add package AgentBlazor --version 0.2.23-internal.1 --source agentblazor-0.2.23-internal.1
```

## Migrating an existing flat feed

If the root already contains flat nupkgs from a previous publish, the script archives them automatically on the next run (step 4). No manual move needed.

## Environment variable

| Variable | Scope | Default |
|---|---|---|
| `AGENTBLAZOR_LOCAL_FEED` | User | `%USERPROFILE%\.agentblazor-feed` |
