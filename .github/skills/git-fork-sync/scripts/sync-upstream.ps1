<#
.SYNOPSIS
    Syncs a git fork/clone from its upstream remote and integrates upstream
    changes into a divergent development branch (mirror/merge model).

.DESCRIPTION
    Two modes:

      Mirror    - Fast-forwards the local mirror branch (default: master) to the
                  upstream branch. The mirror never carries local work, so this
                  mode REFUSES to run on a dirty working tree.
      Integrate - Refreshes the mirror from upstream, checks out the development
                  branch (default: develop), and merges the mirror into it.
                  Uncommitted work on the development branch is stashed (with -u)
                  and restored before the divergence merge.

    Repo-agnostic: pass -UpstreamUrl once to create the upstream remote, or rely
    on an existing upstream remote. Run with -DryRun first to preview every
    mutating command without changing anything.

.PARAMETER UpstreamUrl
    URL of the upstream repository. Optional if an upstream remote already exists.

.PARAMETER UpstreamRemote
    Name used for the upstream remote. Defaults to "upstream".

.PARAMETER MirrorBranch
    Branch that mirrors upstream. Defaults to "master".

.PARAMETER DevBranch
    Branch holding your divergence (Integrate mode). Defaults to "develop".

.PARAMETER Mode
    "Mirror" (default) or "Integrate".

.PARAMETER PushToOrigin
    Push the resulting branch to origin after a successful sync.

.PARAMETER ForceWithLease
    Push with --force-with-lease.

.PARAMETER DryRun
    Print the mutating commands that would run, without executing them.
    Read-only git calls (status, branch, stash list, etc.) still run for an accurate preview.

.PARAMETER KeepStash
    Leave the auto-created stash in place after syncing instead of popping it.

.EXAMPLE
    powershell -File scripts/sync-upstream.ps1 -Mode Mirror -UpstreamUrl https://github.com/owner/repo.git
    powershell -File scripts/sync-upstream.ps1 -Mode Mirror -PushToOrigin
    powershell -File scripts/sync-upstream.ps1 -Mode Integrate
    powershell -File scripts/sync-upstream.ps1 -Mode Integrate -PushToOrigin
    powershell -File scripts/sync-upstream.ps1 -Mode Mirror -DryRun
#>

[CmdletBinding()]
param(
    [string]$UpstreamUrl = "",
    [string]$UpstreamRemote = "upstream",
    [string]$MirrorBranch = "master",
    [string]$DevBranch = "develop",
    [ValidateSet("Mirror", "Integrate")]
    [string]$Mode = "Mirror",
    [switch]$PushToOrigin,
    [switch]$ForceWithLease,
    [switch]$DryRun,
    [switch]$KeepStash
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Status {
    # User-facing progress for this interactive CLI; operational detail uses Write-Verbose.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [ValidateSet("Header", "Info", "Success", "Warning", "Error")]
        [string]$Kind = "Info"
    )

    $color = switch ($Kind) {
        "Header"  { "Cyan" }
        "Info"    { "Gray" }
        "Success" { "Green" }
        "Warning" { "Yellow" }
        "Error"   { "Red" }
    }
    Write-Host $Message -ForegroundColor $color
}

function Invoke-Git {
    <#
    .SYNOPSIS
        Runs a git command, capturing output and failing loudly on non-zero exit codes.
    .PARAMETER Arguments
        Git arguments (e.g. @("fetch", "upstream", "master")).
    .PARAMETER DryRun
        Print the command instead of running it.
    .PARAMETER NoThrow
        Return $null on failure instead of throwing (for optional/read-only probes).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$DryRun,
        [switch]$NoThrow
    )

    $commandLine = "git $($Arguments -join ' ')"

    if ($DryRun) {
        Write-Status "DRY RUN: $commandLine" -Kind Warning
        return $null
    }

    Write-Verbose "Running: $commandLine"

    # Run with EAP=Continue so native stderr (e.g. "error: No such remote 'upstream'")
    # is captured as output records instead of aborting under EAP=Stop (Windows
    # PowerShell turns redirected native stderr into a terminating error otherwise).
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & git @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousEap
    }
    $output = @($output | ForEach-Object { "$_" })

    if ($exitCode -ne 0) {
        $failure = "`"$commandLine`" failed with exit code $exitCode.`n$($output -join "`n")"
        if ($NoThrow) {
            Write-Verbose $failure
            return $null
        }
        throw $failure
    }

    return $output
}

function Test-GitInstalled {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "git executable not found on PATH. Install Git and retry."
    }
}

function Get-CurrentBranch {
    # Returns the current branch name, or an empty string on a detached HEAD.
    $branch = Invoke-Git -Arguments @("branch", "--show-current")
    return "$($branch | Select-Object -First 1)"
}

function Test-RepoDirty {
    # True when there are modified or untracked files in the working tree.
    $porcelain = Invoke-Git -Arguments @("status", "--porcelain")
    return [bool](@($porcelain | Where-Object { $_ }).Count -gt 0)
}

function Get-StashCount {
    $stashes = Invoke-Git -Arguments @("stash", "list")
    return @($stashes | Where-Object { $_ }).Count
}

function Add-UpstreamRemoteIfMissing {
    <#
    .SYNOPSIS
        Ensures the upstream remote exists; creates it with -UpstreamUrl when absent.
    #>
    [CmdletBinding()]
    param(
        [string]$UpstreamUrl,
        [string]$UpstreamRemote = "upstream",
        [switch]$DryRun
    )

    $existingUrl = Invoke-Git -Arguments @("remote", "get-url", $UpstreamRemote) -NoThrow
    if ($existingUrl) {
        Write-Status "Upstream remote '$UpstreamRemote' already exists: $($existingUrl -join ' ')" -Kind Info
        return
    }

    if (-not $UpstreamUrl) {
        throw "No upstream remote '$UpstreamRemote' is configured and no -UpstreamUrl was provided. Run once with -UpstreamUrl <repo-url> to create it."
    }

    Write-Status "Adding upstream remote '$UpstreamRemote' -> $UpstreamUrl" -Kind Header
    Invoke-Git -Arguments @("remote", "add", $UpstreamRemote, $UpstreamUrl) -DryRun:$DryRun | Out-Null
    if ($DryRun) { Write-Status "Would add upstream remote." -Kind Info } else { Write-Status "Upstream remote added." -Kind Success }
}

function Write-SyncSummary {
    <#
    .SYNOPSIS
        Reports ahead/behind counts vs upstream and the final dry-run/success banner.
    #>
    [CmdletBinding()]
    param(
        [string]$UpstreamRemote = "upstream",
        [string]$MirrorBranch = "master",
        [switch]$DryRun
    )

    $upstreamRef = "$UpstreamRemote/$MirrorBranch"
    $counts = Invoke-Git -Arguments @("rev-list", "--left-right", "--count", "HEAD...$upstreamRef") -NoThrow
    if ($counts) {
        $parts = @(($counts -join " ") -split "\s+" | Where-Object { $_ })
        if ($parts.Count -eq 2) {
            $ahead = [int]$parts[0]   # commits in HEAD not in upstream
            $behind = [int]$parts[1]  # commits in upstream not in HEAD
            Write-Status "Status vs $upstreamRef`: $behind commit(s) behind, $ahead commit(s) ahead." -Kind Info
        }
    }

    if ($DryRun) {
        Write-Status "Dry run complete. No changes were made to the repository." -Kind Warning
    }
    else {
        Write-Status "Done." -Kind Success
    }
}

function Invoke-MirrorSync {
    <#
    .SYNOPSIS
        Fast-forwards the mirror branch to upstream. Requires a clean working tree.
    #>
    [CmdletBinding()]
    param(
        [string]$UpstreamUrl,
        [string]$UpstreamRemote = "upstream",
        [string]$MirrorBranch = "master",
        [switch]$PushToOrigin,
        [switch]$ForceWithLease,
        [switch]$DryRun
    )

    Test-GitInstalled

    Write-Status "Mirror: syncing '$MirrorBranch' from upstream '$UpstreamRemote'." -Kind Header
    if ($DryRun) {
        Write-Status "DRY RUN MODE: mutating commands are previewed, not executed." -Kind Warning
    }

    Add-UpstreamRemoteIfMissing -UpstreamUrl $UpstreamUrl -UpstreamRemote $UpstreamRemote -DryRun:$DryRun

    # The mirror must stay pristine so syncs remain fast-forwards.
    if (Test-RepoDirty) {
        throw "Working tree is dirty; '$MirrorBranch' is a mirror and must stay clean. Commit or stash your changes, then re-run."
    }

    $currentBranch = Get-CurrentBranch
    if ($currentBranch -and $currentBranch -ne $MirrorBranch) {
        Write-Status "Checking out '$MirrorBranch'..." -Kind Info
        Invoke-Git -Arguments @("checkout", $MirrorBranch) -DryRun:$DryRun | Out-Null
    }

    $upstreamRef = "$UpstreamRemote/$MirrorBranch"
    Write-Status "Fetching $upstreamRef..." -Kind Info
    Invoke-Git -Arguments @("fetch", $UpstreamRemote, $MirrorBranch) -DryRun:$DryRun | Out-Null

    Write-Status "Fast-forwarding '$MirrorBranch' to $upstreamRef..." -Kind Info
    Invoke-Git -Arguments @("merge", "--ff-only", $upstreamRef) -DryRun:$DryRun | Out-Null
    if ($DryRun) { Write-Status "Would fast-forward '$MirrorBranch'." -Kind Info } else { Write-Status "'$MirrorBranch' is up to date with $upstreamRef." -Kind Success }

    if ($PushToOrigin) {
        $pushArgs = @("push", "origin", $MirrorBranch)
        if ($ForceWithLease) { $pushArgs += "--force-with-lease" }
        Write-Status "Pushing '$MirrorBranch' to origin..." -Kind Info
        Invoke-Git -Arguments $pushArgs -DryRun:$DryRun | Out-Null
        if ($DryRun) { Write-Status "Would push to origin." -Kind Info } else { Write-Status "Pushed to origin." -Kind Success }
    }

    Write-SyncSummary -UpstreamRemote $UpstreamRemote -MirrorBranch $MirrorBranch -DryRun:$DryRun
}

function Invoke-IntegrateSync {
    <#
    .SYNOPSIS
        Refreshes the mirror from upstream, then merges it into the dev branch.
        Uncommitted work on the dev branch is stashed and restored.
    #>
    [CmdletBinding()]
    param(
        [string]$UpstreamUrl,
        [string]$UpstreamRemote = "upstream",
        [string]$MirrorBranch = "master",
        [string]$DevBranch = "develop",
        [switch]$PushToOrigin,
        [switch]$ForceWithLease,
        [switch]$DryRun,
        [switch]$KeepStash
    )

    Test-GitInstalled

    Write-Status "Integrate: merging upstream into '$DevBranch' via mirror '$MirrorBranch'." -Kind Header
    if ($DryRun) {
        Write-Status "DRY RUN MODE: mutating commands are previewed, not executed." -Kind Warning
    }

    Add-UpstreamRemoteIfMissing -UpstreamUrl $UpstreamUrl -UpstreamRemote $UpstreamRemote -DryRun:$DryRun

    # Integrate targets the dev branch; require being on it.
    $currentBranch = Get-CurrentBranch
    if (-not $currentBranch) {
        throw "Detached HEAD detected. Checkout '$DevBranch' before running Integrate."
    }
    if ($currentBranch -ne $DevBranch) {
        throw "Integrate must run from '$DevBranch' but current branch is '$currentBranch'. Checkout '$DevBranch' first (or pass -DevBranch)."
    }

    # Stash uncommitted work so the mirror checkout applies cleanly.
    $stashCountBefore = Get-StashCount
    $stashed = $false
    if (Test-RepoDirty) {
        Write-Status "Working tree is dirty; stashing changes (including untracked files)..." -Kind Info
        Invoke-Git -Arguments @("stash", "push", "-u", "-m", "sync-upstream: integrate auto-stash") -DryRun:$DryRun | Out-Null
        $stashed = (Get-StashCount -gt $stashCountBefore)
    }
    else {
        Write-Status "Working tree is clean; nothing to stash." -Kind Info
    }

    try {
        # 1. Refresh the mirror from upstream (fast-forward by construction).
        if ($currentBranch -ne $MirrorBranch) {
            Write-Status "Checking out mirror '$MirrorBranch'..." -Kind Info
            Invoke-Git -Arguments @("checkout", $MirrorBranch) -DryRun:$DryRun | Out-Null
        }
        $upstreamRef = "$UpstreamRemote/$MirrorBranch"
        Write-Status "Fetching $upstreamRef..." -Kind Info
        Invoke-Git -Arguments @("fetch", $UpstreamRemote, $MirrorBranch) -DryRun:$DryRun | Out-Null
        Write-Status "Fast-forwarding mirror '$MirrorBranch'..." -Kind Info
        Invoke-Git -Arguments @("merge", "--ff-only", $upstreamRef) -DryRun:$DryRun | Out-Null

        # 2. Back on the dev branch; restore local work BEFORE the divergence merge
        #    so conflict markers are the only artifact to resolve.
        Write-Status "Checking out '$DevBranch'..." -Kind Info
        Invoke-Git -Arguments @("checkout", $DevBranch) -DryRun:$DryRun | Out-Null

        if ($stashed -and -not $KeepStash) {
            Write-Status "Restoring stashed changes..." -Kind Info
            Invoke-Git -Arguments @("stash", "pop") -DryRun:$DryRun | Out-Null
            $stashed = $false
            if ($DryRun) { Write-Status "Would restore stash." -Kind Info } else { Write-Status "Stash restored." -Kind Success }
        }
        elseif ($stashed) {
            Write-Status "Stash kept (per -KeepStash). Restore later with 'git stash pop'." -Kind Warning
        }

        # 3. The divergence merge: upstream changes land here, once.
        Write-Status "Merging '$MirrorBranch' into '$DevBranch'..." -Kind Info
        Invoke-Git -Arguments @("merge", $MirrorBranch) -DryRun:$DryRun | Out-Null
        if ($DryRun) { Write-Status "Would merge mirror into dev." -Kind Info } else { Write-Status "Merged '$MirrorBranch' into '$DevBranch'." -Kind Success }

        if ($PushToOrigin) {
            $pushArgs = @("push", "origin", $DevBranch)
            if ($ForceWithLease) { $pushArgs += "--force-with-lease" }
            Write-Status "Pushing '$DevBranch' to origin..." -Kind Info
            Invoke-Git -Arguments $pushArgs -DryRun:$DryRun | Out-Null
            if ($DryRun) { Write-Status "Would push to origin." -Kind Info } else { Write-Status "Pushed to origin." -Kind Success }
        }
    }
    catch {
        if ($stashed) {
            Write-Status "Sync failed while your changes were stashed. They are safe; restore with 'git stash pop'." -Kind Warning
        }
        else {
            Write-Status "Sync failed (likely a merge conflict). Your changes were already restored; resolve conflicts and commit." -Kind Warning
        }
        throw
    }

    Write-SyncSummary -UpstreamRemote $UpstreamRemote -MirrorBranch $MirrorBranch -DryRun:$DryRun
}

# Execute when run as a script; skip when dot-sourced (allows Pester tests to load the functions).
if ($MyInvocation.InvocationName -ne ".") {
    switch ($Mode) {
        "Mirror" {
            Invoke-MirrorSync `
                -UpstreamUrl $UpstreamUrl `
                -UpstreamRemote $UpstreamRemote `
                -MirrorBranch $MirrorBranch `
                -PushToOrigin:$PushToOrigin `
                -ForceWithLease:$ForceWithLease `
                -DryRun:$DryRun
        }
        "Integrate" {
            Invoke-IntegrateSync `
                -UpstreamUrl $UpstreamUrl `
                -UpstreamRemote $UpstreamRemote `
                -MirrorBranch $MirrorBranch `
                -DevBranch $DevBranch `
                -PushToOrigin:$PushToOrigin `
                -ForceWithLease:$ForceWithLease `
                -DryRun:$DryRun `
                -KeepStash:$KeepStash
        }
    }
}
