<#
.SYNOPSIS
    Pester tests for .github/skills/git-fork-sync/scripts/sync-upstream.ps1

.DESCRIPTION
    Dot-sources the script to expose its functions, then mocks Invoke-Git so the
    orchestration logic can be tested without touching a real repository.

    NOTE: Run with Pester 5.x. This machine also has Pester 6.0.1 and 3.4.0
    installed; Pester 6.0.1 currently fails to register Should operators here.
    To run:
        Import-Module Pester -RequiredVersion 5.6.1
        Invoke-Pester -Path .github\skills\git-fork-sync\scripts\sync-upstream.Tests.ps1
#>

Describe "sync-upstream.ps1" {
    BeforeAll {
        . "$PSScriptRoot/sync-upstream.ps1"

        $script:mockCalls = [System.Collections.Generic.List[string]]::new()
        $script:stashPushed = $false

        # Simulated git responses, keyed by substring patterns of the full git command line.
        # - $null        -> command succeeds with no output
        # - string/array -> command succeeds with that output
        # - "__THROW__"  -> command fails (mimics a non-zero git exit code)
        # - scriptblock  -> invoked for a dynamic response
        $script:gitResponses = [ordered]@{
            "remote get-url upstream"  = $null
            "remote add"               = "added upstream"
            "branch --show-current"    = "master"
            "status --porcelain"       = @()
            "stash list"               = { if ($script:stashPushed) { @("stash@{0}: On develop: mock stash") } else { @() } }
            "stash push"               = { $script:stashPushed = $true; return $null }
            "stash pop"                = { $script:stashPushed = $false; return $null }
            "fetch upstream master"    = $null
            "checkout master"          = $null
            "checkout develop"         = $null
            "merge --ff-only upstream/master" = $null
            "merge master"             = $null
            "push origin"              = $null
            "rev-list"                 = "0 2"
        }

        Mock Invoke-Git {
            param($Arguments, [switch]$DryRun, [switch]$NoThrow)

            $key = $Arguments -join ' '
            if (-not $DryRun) {
                $script:mockCalls.Add($key)
            }

            # Dry runs must never execute anything (mirrors the real Invoke-Git contract).
            if ($DryRun) {
                return $null
            }

            foreach ($pattern in $script:gitResponses.Keys) {
                if ($key -like "*$pattern*") {
                    $value = $script:gitResponses[$pattern]
                    if ($value -eq "__THROW__") {
                        throw "Mocked git failure: $key"
                    }
                    if ($value -is [scriptblock]) {
                        return & $value
                    }
                    return $value
                }
            }

            if ($NoThrow) {
                return $null
            }
            throw "Unexpected git invocation in test: $key"
        }
    }

    BeforeEach {
        $script:mockCalls.Clear()
        $script:stashPushed = $false
        $script:gitResponses["remote get-url upstream"] = $null
        $script:gitResponses["status --porcelain"] = @()
        $script:gitResponses["branch --show-current"] = "master"
        $script:gitResponses["merge --ff-only upstream/master"] = $null
        $script:gitResponses["merge master"] = $null
        $script:gitResponses["stash pop"] = { $script:stashPushed = $false; return $null }
        $script:gitResponses["rev-list"] = "0 2"
    }

    Context "Mirror mode" {
        It "adds the upstream remote when it does not exist" {
            { Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 1 -ParameterFilter {
                $Arguments[0] -eq "remote" -and $Arguments[1] -eq "add"
            }
        }

        It "throws when no upstream remote exists and no URL is provided" {
            { Invoke-MirrorSync } | Should -Throw "*UpstreamUrl*"
        }

        It "does not add the upstream remote when it already exists" {
            $script:gitResponses["remote get-url upstream"] = "https://github.com/ashpeterson/AgentBlazor.git"
            { Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 0 -ParameterFilter {
                $Arguments[0] -eq "remote" -and $Arguments[1] -eq "add"
            }
        }

        It "refuses to run on a dirty working tree" {
            $script:gitResponses["status --porcelain"] = @(" M demo/AgentBlazor.Demo/appsettings.Development.json")
            { Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Throw "*mirror*"
            Assert-MockCalled Invoke-Git -Times 0 -ParameterFilter { $Arguments[0] -eq "fetch" }
        }

        It "fast-forwards the mirror with --ff-only" {
            { Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 1 -ParameterFilter {
                $Arguments[0] -eq "merge" -and $Arguments -contains "--ff-only"
            }
        }

        It "checks out the mirror branch when on another branch" {
            $script:gitResponses["branch --show-current"] = "develop"
            { Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 1 -ParameterFilter { $Arguments -join ' ' -eq "checkout master" }
        }

        It "does not checkout when already on the mirror branch" {
            $script:gitResponses["branch --show-current"] = "master"
            { Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 0 -ParameterFilter { $Arguments[0] -eq "checkout" }
        }

        It "pushes the mirror to origin only with -PushToOrigin" {
            { Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" -PushToOrigin } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 1 -ParameterFilter {
                $Arguments[0] -eq "push" -and $Arguments[1] -eq "origin" -and $Arguments[2] -eq "master"
            }
            { Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 1 -ParameterFilter { $Arguments[0] -eq "push" }
        }

        It "dry run performs no mutating git commands" {
            { Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" -DryRun -PushToOrigin } | Should -Not -Throw
            $mutating = $script:mockCalls | Where-Object { $_ -match "^(remote add|checkout|fetch|merge|stash push|stash pop|push origin)" }
            $mutating | Should -BeNullOrEmpty
        }

        It "reports how far ahead/behind the branch is versus upstream" {
            $script:gitResponses["rev-list"] = "0 2"
            $output = Invoke-MirrorSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" 6>&1
            ($output | Out-String) | Should -Match "2 commit\(s\) behind, 0 commit\(s\) ahead"
        }
    }

    Context "Integrate mode" {
        It "throws when not on the dev branch" {
            $script:gitResponses["branch --show-current"] = "feature/foo"
            { Invoke-IntegrateSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Throw "*must run from 'develop'*"
        }

        It "throws on a detached HEAD" {
            $script:gitResponses["branch --show-current"] = $null
            { Invoke-IntegrateSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Throw "*Detached HEAD*"
        }

        It "stashes dirty work, refreshes the mirror, and restores before the divergence merge" {
            $script:gitResponses["branch --show-current"] = "develop"
            $script:gitResponses["status --porcelain"] = @(" M demo/AgentBlazor.Demo/appsettings.Development.json")
            { Invoke-IntegrateSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Not -Throw

            # Order matters: stash -> mirror refresh -> restore -> divergence merge
            $calls = @($script:mockCalls)
            ($calls | Where-Object { $_ -like "stash push*" }) | Should -Not -BeNullOrEmpty
            $calls.IndexOf("checkout master") | Should -BeLessThan $calls.IndexOf("stash pop")
            $calls.IndexOf("merge --ff-only upstream/master") | Should -BeLessThan $calls.IndexOf("stash pop")
            $calls.IndexOf("stash pop") | Should -BeLessThan $calls.IndexOf("merge master")
        }

        It "does not stash when the working tree is clean" {
            $script:gitResponses["branch --show-current"] = "develop"
            { Invoke-IntegrateSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 0 -ParameterFilter { $Arguments[0] -eq "stash" -and $Arguments[1] -eq "push" }
        }

        It "restores the stash before the merge, so a merge failure leaves conflicts, not a stash" {
            $script:gitResponses["branch --show-current"] = "develop"
            $script:gitResponses["status --porcelain"] = @(" M demo/AgentBlazor.Demo/appsettings.Development.json")
            $script:gitResponses["merge master"] = "__THROW__"
            { Invoke-IntegrateSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Throw
            Assert-MockCalled Invoke-Git -Times 1 -ParameterFilter {
                $Arguments[0] -eq "stash" -and $Arguments[1] -eq "pop"
            }
        }

        It "leaves the stash in place when the stash pop itself fails" {
            $script:gitResponses["branch --show-current"] = "develop"
            $script:gitResponses["status --porcelain"] = @(" M demo/AgentBlazor.Demo/appsettings.Development.json")
            $script:gitResponses["stash pop"] = "__THROW__"
            { Invoke-IntegrateSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" } | Should -Throw
            $script:stashPushed | Should -BeTrue
        }

        It "pushes the dev branch to origin only with -PushToOrigin" {
            $script:gitResponses["branch --show-current"] = "develop"
            { Invoke-IntegrateSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" -PushToOrigin } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 1 -ParameterFilter {
                $Arguments[0] -eq "push" -and $Arguments[1] -eq "origin" -and $Arguments[2] -eq "develop"
            }
        }

        It "keeps the stash with -KeepStash" {
            $script:gitResponses["branch --show-current"] = "develop"
            $script:gitResponses["status --porcelain"] = @(" M demo/AgentBlazor.Demo/appsettings.Development.json")
            { Invoke-IntegrateSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" -KeepStash } | Should -Not -Throw
            Assert-MockCalled Invoke-Git -Times 0 -ParameterFilter {
                $Arguments[0] -eq "stash" -and $Arguments[1] -eq "pop"
            }
        }

        It "dry run performs no mutating git commands" {
            $script:gitResponses["branch --show-current"] = "develop"
            $script:gitResponses["status --porcelain"] = @(" M demo/AgentBlazor.Demo/appsettings.Development.json")
            { Invoke-IntegrateSync -UpstreamUrl "https://github.com/ashpeterson/AgentBlazor.git" -DryRun -PushToOrigin } | Should -Not -Throw
            $mutating = $script:mockCalls | Where-Object { $_ -match "^(remote add|checkout|fetch|merge|stash push|stash pop|push origin)" }
            $mutating | Should -BeNullOrEmpty
        }
    }
}
