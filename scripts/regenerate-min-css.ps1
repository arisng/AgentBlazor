<#
.SYNOPSIS
    Regenerates src/AgentBlazor.Components/wwwroot/AgentBlazor.min.css from the Razor
    SDK's compiled scoped-CSS bundle, so the shipped "min.css" can never drift from the
    component .razor.css sources again.

.DESCRIPTION
    The Razor compiler already bundles every .razor.css file in the RCL into
    obj/<tfm>/scopedcss/bundle/AgentBlazor.styles.css during every build. That bundle is
    the single source of truth for component styles. This script:

      1. Reads a compiled scoped bundle (auto-discovers the newest one under
         src/AgentBlazor.Components/obj when -ScopedBundle is omitted).
      2. Strips the Blazor CSS-isolation scope attribute selectors ([b-<hash>]) so the
         output stays an un-scoped, "works everywhere" stylesheet — exactly the contract
         of the legacy hand-maintained AgentBlazor.min.css (applies even to HTML rendered
         without scope attributes, e.g. MarkupString content or non-Blazor host pages).
      3. Minifies (comments + insignificant whitespace, string-aware) and writes
         wwwroot/AgentBlazor.min.css (UTF-8, no BOM), skipping the write when the content
         is unchanged so build timestamps/fingerprints stay stable.

    The MSBuild target RegenerateAgentBlazorMinCss in AgentBlazor.Components.csproj calls
    this after BundleScopedCssFiles and before GenerateStaticWebAssetsManifest, so the
    regenerated file is fingerprinted and packed with fresh content.

.PARAMETER ScopedBundle
    Path to a compiled scoped bundle (AgentBlazor.styles.css). When omitted, the newest
    bundle under src/AgentBlazor.Components/obj is used.

.PARAMETER OutputFile
    Destination for the generated stylesheet. Defaults to
    src/AgentBlazor.Components/wwwroot/AgentBlazor.min.css.

.PARAMETER Check
    CI mode: regenerate in memory and fail (exit 1) if the committed file differs from
    what the current sources would produce. Use this to enforce freshness of the file
    that is committed to git / packed into the NuGet package.

.EXAMPLE
    # Regenerate (and overwrite) the committed stylesheet after editing a .razor.css:
    ./scripts/regenerate-min-css.ps1

.EXAMPLE
    # Fail if the committed stylesheet is stale (CI):
    ./scripts/regenerate-min-css.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$ScopedBundle,
    [string]$OutputFile,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $ScopedBundle) {
    $candidates = Get-ChildItem -Path (Join-Path $repoRoot 'src\AgentBlazor.Components\obj') -Recurse -Filter 'AgentBlazor.styles.css' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending
    if (-not $candidates) {
        throw "No scoped CSS bundle found under src/AgentBlazor.Components/obj. Build the project first (dotnet build src/AgentBlazor.Components), then retry."
    }
    $ScopedBundle = $candidates[0].FullName
}

if (-not (Test-Path -LiteralPath $ScopedBundle)) {
    throw "Scoped bundle not found: $ScopedBundle"
}

if (-not $OutputFile) {
    $OutputFile = Join-Path $repoRoot 'src\AgentBlazor.Components\wwwroot\AgentBlazor.min.css'
}

function ConvertTo-MinifiedCss {
    <#
    Conservative, string-aware CSS minifier. Safe for the subset of CSS this repo emits
    (no url() data URIs, no space-before-colon pseudo-class selectors). It:
      - strips /* */ comments
      - collapses whitespace runs outside strings
      - drops whitespace adjacent to { } ; , > + ~ ( ) and (inside declaration blocks) :
      - preserves whitespace inside strings and inside selector lists at brace depth 0
    #>
    param([string]$Css)

    $sb = [System.Text.StringBuilder]::new($Css.Length)
    $i = 0
    $n = $Css.Length
    $inString = [char]0
    $braceDepth = 0
    $pendingSpace = $false

    $skipSet = [System.Collections.Generic.HashSet[char]]::new()
    foreach ($c in [char[]]'{ };,>+~()') { [void]$skipSet.Add($c) }

    while ($i -lt $n) {
        $ch = $Css[$i]

        # Comment
        if ($ch -eq '/' -and ($i + 1) -lt $n -and $Css[$i + 1] -eq '*') {
            $end = $Css.IndexOf('*/', $i + 2)
            if ($end -lt 0) { $i = $n } else { $i = $end + 2 }
            continue
        }

        # Inside a string: copy verbatim (handle backslash escapes)
        if ($inString -ne [char]0) {
            [void]$sb.Append($ch)
            if ($ch -eq '\' -and ($i + 1) -lt $n) {
                [void]$sb.Append($Css[$i + 1])
                $i += 2
                continue
            }
            if ($ch -eq $inString) { $inString = [char]0 }
            $i++
            continue
        }

        # String start
        if ($ch -eq '"' -or $ch -eq "'") {
            $inString = $ch
            [void]$sb.Append($ch)
            $i++
            continue
        }

        # Whitespace: remember it, decide whether to emit later
        if ([char]::IsWhiteSpace($ch)) {
            $pendingSpace = $true
            $i++
            continue
        }

        if ($pendingSpace) {
            $prev = if ($sb.Length -gt 0) { $sb[$sb.Length - 1] } else { [char]0 }
            $drop = $false
            if ($prev -ne [char]0 -and ($skipSet.Contains($prev) -or ($prev -eq ':' -and $braceDepth -gt 0))) { $drop = $true }
            if ($skipSet.Contains($ch) -or ($ch -eq ':' -and $braceDepth -gt 0)) { $drop = $true }
            if (-not $drop) { [void]$sb.Append(' ') }
            $pendingSpace = $false
        }

        if ($ch -eq '{') { $braceDepth++ }
        elseif ($ch -eq '}') { $braceDepth = [Math]::Max(0, $braceDepth - 1) }

        [void]$sb.Append($ch)
        $i++
    }

    $out = $sb.ToString()
    $out = $out -replace ';\}', '}'
    return $out.Trim()
}

$source = [System.IO.File]::ReadAllText($ScopedBundle)

# 1. Remove Blazor CSS-isolation scope attribute selectors: [b-<hash>]
$unscoped = [regex]::Replace($source, '\[b-[a-zA-Z0-9]+\]', '')

# 2. Minify
$minified = ConvertTo-MinifiedCss $unscoped
$minified = $minified.TrimEnd()

$existing = if (Test-Path -LiteralPath $OutputFile) { [System.IO.File]::ReadAllText($OutputFile) } else { $null }
$changed = ($existing -ne $minified)

if ($Check) {
    if ($changed) {
        Write-Host "STALE: AgentBlazor.min.css does not match the compiled scoped bundle."
        Write-Host "       Run ./scripts/regenerate-min-css.ps1 and commit the regenerated file."
        Write-Host "       Bundle: $ScopedBundle"
        exit 1
    }
    Write-Host "OK: AgentBlazor.min.css is fresh."
    exit 0
}

if (-not $changed) {
    Write-Host "AgentBlazor.min.css is up to date (source: $ScopedBundle)."
    exit 0
}

$outDir = Split-Path -Parent $OutputFile
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($OutputFile, $minified, $utf8NoBom)

$oldLen = if ($null -ne $existing) { $existing.Length } else { 0 }
Write-Host "Regenerated $OutputFile ($oldLen -> $($minified.Length) chars) from $ScopedBundle"
