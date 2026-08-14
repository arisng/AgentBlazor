<#
.SYNOPSIS
Publishes the AgentBlazor package set to the private local NuGet feed with an enforced
per-version folder layout.

.DESCRIPTION
Creates <feed>\<version>\ holding every .nupkg/.snupkg of the target version, archives any
older flat nupkgs at the feed root into their own version folders, and keeps a flat mirror of
the CURRENT version at the feed root so the root remains a valid NuGet source.

Why this layout: NuGet folder feeds auto-discover nupkgs that are flat in a folder, or in the
hierarchical <id>\<version>\ layout. Version-first grouping (<feed>\<version>\*.nupkg) is NOT
auto-discovered at the root. Each version folder is therefore a valid source on its own (add it
with `dotnet nuget add source <feed>\<version>`), and the root keeps the current version flat as
the stable primary source. The version folder doubles as the organized archive.

.PARAMETER Version
Target package version (e.g. 0.2.23-internal.1). Defaults to the <Version> in Directory.Build.props.

.PARAMETER Pack
Pack the package set at $Version before publishing. Without it, existing bin\Release nupkgs are used.

.PARAMETER FeedRoot
Feed folder. Defaults to the User-level AGENTBLAZOR_LOCAL_FEED env var, else $HOME\.agentblazor-feed.

.PARAMETER DryRun
Print the planned operations without executing any of them.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\publish-private-feed.ps1 -Pack -Version 0.2.23-internal.1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\publish-private-feed.ps1 -DryRun -Verbose
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$Pack,
    [string]$FeedRoot = "",
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:PackageVersionPattern = '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$'

function Get-BuildPropsVersion {
    param(
        [Parameter(Mandatory)]
        [ValidateScript({ Test-Path $_ })]
        [string]$RepoRoot
    )

    $propsPath = Join-Path $RepoRoot "Directory.Build.props"
    if (-not (Test-Path $propsPath)) {
        throw "Directory.Build.props not found at $propsPath"
    }

    $content = Get-Content $propsPath -Raw
    if ($content -match '<Version>([^<]+)</Version>') {
        return $matches[1].Trim()
    }
    throw "Could not parse <Version> from $propsPath"
}

function Test-PackageVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Version
    )

    return ($Version -match $script:PackageVersionPattern)
}

function Get-PackageDefinitions {
    $base = $script:RepoRoot
    return @(
        [pscustomobject]@{ Id = "AgentBlazor.Licensing";        Project = Join-Path $base "src\AgentBlazor.Licensing\AgentBlazor.Licensing.csproj";         OutputDir = Join-Path $base "src\AgentBlazor.Licensing\bin\Release" },
        [pscustomobject]@{ Id = "AgentBlazor.Core";             Project = Join-Path $base "src\AgentBlazor.Core\AgentBlazor.Core.csproj";                   OutputDir = Join-Path $base "src\AgentBlazor.Core\bin\Release" },
        [pscustomobject]@{ Id = "AgentBlazor.ProviderAdapters"; Project = Join-Path $base "src\AgentBlazor.ProviderAdapters\AgentBlazor.ProviderAdapters.csproj"; OutputDir = Join-Path $base "src\AgentBlazor.ProviderAdapters\bin\Release" },
        [pscustomobject]@{ Id = "AgentBlazor.Hosting";          Project = Join-Path $base "src\AgentBlazor.Hosting\AgentBlazor.Hosting.csproj";             OutputDir = Join-Path $base "src\AgentBlazor.Hosting\bin\Release" },
        [pscustomobject]@{ Id = "AgentBlazor";                  Project = Join-Path $base "src\AgentBlazor.Components\AgentBlazor.Components.csproj";         OutputDir = Join-Path $base "src\AgentBlazor.Components\bin\Release" },
        [pscustomobject]@{ Id = "AgentBlazor.Client";           Project = Join-Path $base "src\AgentBlazor.Client\AgentBlazor.Client.csproj";               OutputDir = Join-Path $base "src\AgentBlazor.Client\bin\Release" },
        [pscustomobject]@{ Id = "AgentBlazor.EntityFrameworkCore"; Project = Join-Path $base "src\AgentBlazor.EntityFrameworkCore\AgentBlazor.EntityFrameworkCore.csproj"; OutputDir = Join-Path $base "src\AgentBlazor.EntityFrameworkCore\bin\Release" },
        [pscustomobject]@{ Id = "AgentBlazor.Cli";              Project = Join-Path $base "src\AgentBlazor.Cli\AgentBlazor.Cli.csproj";                     OutputDir = Join-Path $base "src\AgentBlazor.Cli\bin\Release" }
    )
}

function Get-PackageVersionFromFileName {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [array]$Definitions
    )

    $ordered = $Definitions | Sort-Object { $_.Id.Length } -Descending
    foreach ($def in $ordered) {
        $prefix = "$($def.Id)."
        if ($Name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $candidate = $Name.Substring($prefix.Length)
            $candidate = [System.IO.Path]::GetFileNameWithoutExtension($candidate)
            if ($candidate -match $script:PackageVersionPattern) {
                return $candidate
            }
        }
    }
    return $null
}

function Invoke-PackPackageSet {
    param(
        [Parameter(Mandatory)]
        [array]$Definitions,
        [Parameter(Mandatory)]
        [string]$Version
    )

    $files = @()
    foreach ($def in $Definitions) {
        Write-Verbose "Packing $($def.Id) @ $Version"
        & dotnet pack $def.Project -c Release -nologo "/p:PackageVersion=$Version" 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet pack failed for $($def.Id)"
        }

        $nupkg = Join-Path $def.OutputDir "$($def.Id).$Version.nupkg"
        $snupkg = Join-Path $def.OutputDir "$($def.Id).$Version.snupkg"
        if (Test-Path $nupkg) { $files += $nupkg }
        if (Test-Path $snupkg) { $files += $snupkg }
    }
    return , $files
}

function Find-PackageFiles {
    param(
        [Parameter(Mandatory)]
        [array]$Definitions,
        [Parameter(Mandatory)]
        [string]$Version
    )

    $files = @()
    foreach ($def in $Definitions) {
        $nupkg = Join-Path $def.OutputDir "$($def.Id).$Version.nupkg"
        $snupkg = Join-Path $def.OutputDir "$($def.Id).$Version.snupkg"
        if (Test-Path $nupkg) { $files += $nupkg }
        if (Test-Path $snupkg) { $files += $snupkg }
    }
    return , $files
}

function Invoke-ArchiveRootFiles {
    param(
        [Parameter(Mandatory)]
        [string]$FeedRoot,
        [Parameter(Mandatory)]
        [string]$Version,
        [Parameter(Mandatory)]
        [array]$Definitions,
        [switch]$DryRun
    )

    Get-ChildItem -Path $FeedRoot -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.nupkg', '.snupkg' } |
        ForEach-Object {
            $fileVersion = Get-PackageVersionFromFileName -Name $_.Name -Definitions $Definitions
            if ($fileVersion -and $fileVersion -ne $Version) {
                $destDir = Join-Path $FeedRoot $fileVersion
                $dest = Join-Path $destDir $_.Name
                if ($DryRun) {
                    Write-Verbose "[DRY-RUN] would archive $($_.Name) -> $fileVersion\"
                }
                else {
                    New-Item -ItemType Directory -Force $destDir | Out-Null
                    Move-Item -Path $_.FullName -Destination $dest -Force
                    Write-Verbose "Archived $($_.Name) -> $fileVersion\"
                }
            }
        }
}

function Publish-PrivateFeed {
    [CmdletBinding()]
    param(
        [string]$Version,
        [switch]$Pack,
        [string]$FeedRoot,
        [switch]$DryRun
    )

    if (-not $Version) {
        $Version = Get-BuildPropsVersion -RepoRoot $script:RepoRoot
        Write-Verbose "Resolved version from Directory.Build.props: $Version"
    }
    if (-not (Test-PackageVersion -Version $Version)) {
        throw "Version '$Version' is not a valid semver-ish string (e.g. 0.2.23-internal.1)."
    }

    if (-not $FeedRoot) {
        $FeedRoot = [Environment]::GetEnvironmentVariable("AGENTBLAZOR_LOCAL_FEED", "User")
        if (-not $FeedRoot) {
            $FeedRoot = Join-Path $HOME ".agentblazor-feed"
        }
        Write-Verbose "Resolved feed root: $FeedRoot"
    }
    $FeedRoot = [System.IO.Path]::GetFullPath($FeedRoot)
    if (Test-Path $FeedRoot -PathType Leaf) {
        throw "Feed root exists but is a file: $FeedRoot"
    }

    $definitions = Get-PackageDefinitions

    if ($Pack) {
        $packageFiles = Invoke-PackPackageSet -Definitions $definitions -Version $Version
    }
    else {
        $packageFiles = Find-PackageFiles -Definitions $definitions -Version $Version
    }
    if (-not $packageFiles) {
        throw "No package files found for $Version in bin\Release. Run with -Pack or pack the projects first."
    }
    Write-Verbose "Package files for $Version : $($packageFiles.Count)"

    $versionFolder = Join-Path $FeedRoot $Version

    # 1) Organized per-version folder (archive + exact-version source)
    if ($DryRun) {
        Write-Verbose "[DRY-RUN] would create $versionFolder and copy $($packageFiles.Count) file(s)"
    }
    else {
        New-Item -ItemType Directory -Force $versionFolder | Out-Null
    }
    foreach ($file in $packageFiles) {
        if ($DryRun) {
            Write-Verbose "[DRY-RUN] would copy $(Split-Path $file -Leaf) -> $Version\"
        }
        else {
            Copy-Item -Path $file -Destination $versionFolder -Force
        }
    }

    # 2) Archive older flat root files into their own version folders
    Invoke-ArchiveRootFiles -FeedRoot $FeedRoot -Version $Version -Definitions $definitions -DryRun:$DryRun

    # 3) Flat mirror of the current version at the root (keeps the root a valid source)
    foreach ($file in $packageFiles) {
        if ($DryRun) {
            Write-Verbose "[DRY-RUN] would mirror $(Split-Path $file -Leaf) to feed root"
        }
        else {
            Copy-Item -Path $file -Destination $FeedRoot -Force
        }
    }

    return [pscustomobject]@{
        Version       = $Version
        FeedRoot      = $FeedRoot
        VersionFolder = $versionFolder
        PackageCount  = $packageFiles.Count
        DryRun        = [bool]$DryRun
    }
}

try {
    $result = Publish-PrivateFeed -Version $Version -Pack:$Pack -FeedRoot $FeedRoot -DryRun:$DryRun
    Write-Verbose "Private feed publish complete:"
    Write-Verbose "  Feed root : $($result.FeedRoot)"
    Write-Verbose "  Version   : $($result.Version)"
    Write-Verbose "  Organized : $($result.VersionFolder)  ($($result.PackageCount) file(s))"
    $result
}
catch {
    Write-Error "Publish failed: $_"
    exit 1
}
