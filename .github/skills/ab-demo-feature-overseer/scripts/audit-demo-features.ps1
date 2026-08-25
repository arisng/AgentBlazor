#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Empirical feature auditor for the AgentBlazor.Demo project.

.DESCRIPTION
    Scans demo/AgentBlazor.Demo source (never bin/obj) and emits a JSON report of
    every demoable feature it can find empirically: agents, workflow capability
    classes, agent actions, approval boundaries, clarification returns, component
    demos, routes, scenario catalog entries, middleware, logging endpoints,
    providers, and config gates.

    The audit is EVIDENCE-BASED: a feature is "implemented" only if the script
    finds its wiring in source. Output feeds the features catalog
    (references/demo-features-catalog.md) which is maintained by hand on top of
    this evidence.

.PARAMETER DemoRoot
    Path to the AgentBlazor.Demo project directory.
    Default: <repo root>/demo/AgentBlazor.Demo resolved from the script location.

.PARAMETER OutputPath
    Where to write the JSON report. Default: writes to stdout if omitted.

.EXAMPLE
    pwsh ./scripts/audit-demo-features.ps1 -OutputPath ./audit.json
#>
[CmdletBinding()]
param(
    [string]$DemoRoot,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if (-not $DemoRoot) {
    # skill folder is .github/skills/ab-demo-feature-overseer/scripts/
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
    $DemoRoot = Join-Path $repoRoot 'demo\AgentBlazor.Demo'
}
if (-not (Test-Path (Join-Path $DemoRoot 'AgentBlazor.Demo.csproj'))) {
    throw "Demo project not found at '$DemoRoot'. Pass -DemoRoot explicitly."
}

function Get-SourceFiles {
    Get-ChildItem -Path $DemoRoot -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|wwwroot\\lib)\\' } |
        Where-Object { $_.Extension -in '.cs', '.razor', '.json', '.txt' }
}

$files = Get-SourceFiles
$report = [ordered]@{
    generatedUtc   = (Get-Date).ToUniversalTime().ToString('o')
    demoRoot       = $DemoRoot
    agents         = @()
    workflows      = @()
    capabilities   = @()
    agentActions   = @()
    approvals      = @()
    clarifications = @()
    components     = @()
    routes         = @()
    scenarios      = @()
    middleware     = @()
    logEndpoints   = @()
    providers      = @()
    dataSchemas    = $null
    services       = @()
    configGates    = @()
}

# --- Agents & workflows registered in Program.cs -----------------------------
$programCs = Join-Path $DemoRoot 'Program.cs'
$programText = Get-Content $programCs -Raw

# Each registration is a lambda: AddAgent("Name", agent => { ... }); or
# AddWorkflow<TCap>("Name", agent => { ... });. The registration body never
# contains "});" other than as its own terminator, so lazy-match to "});" is safe.
$regPattern = '(?ms)agentBuilder\.(?:AddAgent|AddWorkflow<(\w+)>)\("(?<name>[^"]+)"\s*,\s*agent\s*=>\s*\{(?<body>.+?)\}\)\s*;'
$agentRegistrations = @()
foreach ($m in [regex]::Matches($programText, $regPattern)) {
    $body = $m.Groups['body'].Value
    $allowedComponents = @()
    # WithAllowedComponents("A", "B", ...) is ONE call with many string args; capture
    # the full parenthesized list (multiline-safe) and pull out every quoted string.
    foreach ($cm in [regex]::Matches($body, '(?s)WithAllowedComponents\(\s*(.*?)\)')) {
        foreach ($sm in [regex]::Matches($cm.Groups[1].Value, '"([^"]+)"')) {
            $allowedComponents += $sm.Groups[1].Value
        }
    }
    $dataSchemas = @()
    foreach ($dm in [regex]::Matches($body, '(?s)WithDataSchemas\(\s*(.*?)\)')) {
        foreach ($sm in [regex]::Matches($dm.Groups[1].Value, '"([^"]+)"')) {
            $dataSchemas += $sm.Groups[1].Value
        }
    }
    $isWorkflow = $m.Groups[1].Success
    $reg = [ordered]@{
        name               = $m.Groups['name'].Value
        kind               = if ($isWorkflow) { 'workflow' } else { 'agent' }
        capabilitiesType   = if ($isWorkflow) { $m.Groups[1].Value } else { $null }
        allowedComponents  = $allowedComponents
        dataSchemas        = $dataSchemas
        hasSharedInstructions = ($body -match 'WithInstructions\(sharedAgentInstructions\)')
        routePrefixes      = @([regex]::Matches($body, 'WithRoutePrefixes\(([^)]*)\)') | ForEach-Object { ($_.Groups[1].Value -replace '"(.*?)"', '$1') })
    }
    $agentRegistrations += $reg
    if ($isWorkflow) {
        $report.workflows += [ordered]@{ capabilitiesClass = $m.Groups[1].Value; agentName = $m.Groups['name'].Value }
    } else {
        $report.agents += $m.Groups['name'].Value
    }
}
$report.agentRegistrations = @($agentRegistrations)

$dataSchemaSets = @()
foreach ($m in [regex]::Matches($programText, 'AddDataSchema\(new AgentDataSchemaSet\s*\{\s*Name\s*=\s*"([^"]+)"')) {
    $dataSchemaSets += $m.Groups[1].Value
}
$report.dataSchemas = [ordered]@{ schemaSets = $dataSchemaSets; registeredOnAgents = $true }

# --- Capability classes and actions ------------------------------------------
# Capability classes can live in any Services/*.cs file (often alongside the
# workflow service), so scan all of them for the [AgentCapability] attribute.
foreach ($file in (Get-ChildItem (Join-Path $DemoRoot 'Services') -Filter '*.cs' | Sort-Object -Property Name)) {
    $text = Get-Content $file.FullName -Raw
    if ($text -notmatch '\[AgentCapability\(') { continue }
    # Match bare [AgentCapability] or [AgentCapability("id", ... Name = "display", ...)] —
    # tolerate extra named attributes (Description, Category) anywhere before the close paren.
    $capAttr = [regex]::Match($text, '\[AgentCapability(?:\(\s*"([^"]+)"([^)]*?)\))?')
    $actions = @()
    # Match each attribute line then look ahead to the method signature
    $attrPattern = '(?m)^\s*\[AgentAction\(\s*"(?<desc>[^"]+)"(?<rest>[^\)]*)\)\s*\]\s*\r?\n\s*(?:public|internal)[^\r\n]*?(?<method>\w+)\s*\('
    foreach ($am in [regex]::Matches($text, $attrPattern)) {
        $rest = $am.Groups['rest'].Value
        $actionId = if ($rest -match 'ActionId\s*=\s*"([^"]+)"') { $Matches[1] } else { $null }
        $requiresApproval = ($rest -match 'RequiresApproval\s*=\s*true')
        $actions += , [ordered]@{
            description       = $am.Groups['desc'].Value
            actionId          = $actionId
            requiresApproval  = $requiresApproval
        }
        if ($requiresApproval) { $report.approvals += "$($file.BaseName):$actionId" }
    }
    $clarifCount = ([regex]::Matches($text, 'CapabilityResult\.NeedsClarification')).Count
    if ($clarifCount -gt 0) { $report.clarifications += "$($file.BaseName):$clarifCount" }

    $report.capabilities += , [ordered]@{
        class             = $file.BaseName
        capabilityId      = $capAttr.Success ? $capAttr.Groups[1].Value : $null
        displayName       = ($capAttr.Groups[2].Value -match 'Name\s*=\s*"([^"]+)"') ? $Matches[1] : $null
        file              = ("Services/" + $file.Name)
        actionCount       = $actions.Count
        actions           = $actions
        needsClarification= $clarifCount
    }
}

# --- Components used in razor pages -------------------------------------------
# A component tag is <AgentX ...> or <AgentX/>. Require the char after the name to
# be whitespace or '/', so `Task<AgentBlazor.Runtime.ActionResult>` in @code blocks
# is NOT misread as a component ('.' is excluded by the lookahead).
$razorFiles = Get-ChildItem (Join-Path $DemoRoot 'Components') -Recurse -Filter '*.razor'
$componentPattern = '<(Agent[A-Za-z]+)(?=[\s/>])'
$seenComponents = @{}
foreach ($file in $razorFiles) {
    $text = Get-Content $file.FullName -Raw
    $rel = $file.FullName.Substring($DemoRoot.Length + 1).Replace('\', '/')
    foreach ($m in [regex]::Matches($text, $componentPattern)) {
        $name = $m.Groups[1].Value
        if (-not $seenComponents.ContainsKey($name)) {
            $seenComponents[$name] = [ordered]@{ component = $name; files = [System.Collections.Generic.List[string]]::new() }
            $report.components += $seenComponents[$name]
        }
        if (-not $seenComponents[$name].files.Contains($rel)) {
            $seenComponents[$name].files.Add($rel)
        }
    }
}

# --- Routes -------------------------------------------------------------------
foreach ($file in $razorFiles) {
    $text = Get-Content $file.FullName -Raw
    foreach ($m in [regex]::Matches($text, '@page\s+"([^"]+)"')) {
        $rel = $file.FullName.Substring($DemoRoot.Length + 1).Replace('\', '/')
        $report.routes += , [ordered]@{ route = $m.Groups[1].Value; file = $rel }
    }
}

# --- Scenario catalog entries --------------------------------------------------
$catalogFile = Join-Path $DemoRoot 'Services\DemoScenarioCatalog.cs'
if (Test-Path $catalogFile) {
    $cat = Get-Content $catalogFile -Raw
    foreach ($m in [regex]::Matches($cat, 'new\(\s*\n\s+"([a-z0-9-]+)",\s*\n\s+DemoScenarioScale\.(\w+)')) {
        $report.scenarios += , [ordered]@{ id = $m.Groups[1].Value; scale = $m.Groups[2].Value }
    }
}

# --- Middleware -----------------------------------------------------------------
foreach ($m in [regex]::Matches($programText, 'options\.UseMiddleware<(\w+)>')) {
    $report.middleware += $m.Groups[1].Value
}
foreach ($m in [regex]::Matches($programText, 'app\.UseMiddleware<(\w+)>')) {
    $report.middleware += $m.Groups[1].Value
}

# --- Log endpoints ---------------------------------------------------------------
$logMapper = Join-Path $DemoRoot 'Services\DemoLogEndpointMapper.cs'
if (Test-Path $logMapper) {
    $logText = Get-Content $logMapper -Raw
    foreach ($m in [regex]::Matches($logText, 'Map(Get|Post)\("([^"]*)"')) {
        $verb = $m.Groups[1].Value.ToUpperInvariant()
        $path = $m.Groups[2].Value
        $report.logEndpoints += "$verb /demo-logs$path"
    }
}

# --- Providers --------------------------------------------------------------------
$codeOnly = ($programText -replace '(?s)//.*?\n', "`n")
$report.providers += , [ordered]@{
    useOpenAi        = ([regex]::IsMatch($codeOnly, 'UseOpenAI\('))
    useOllama        = ([regex]::IsMatch($codeOnly, 'UseOllama\('))
    proLicense       = ([regex]::IsMatch($codeOnly, 'UseProLicense\('))
    devTools         = ([regex]::IsMatch($codeOnly, 'UseDevTools\('))
    promptTracing    = ([regex]::IsMatch($codeOnly, 'EnablePromptTracing'))
}

# --- Services (workflow services + infra) ----------------------------------------
Get-ChildItem (Join-Path $DemoRoot 'Services') -Filter '*.cs' | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    if ($text -match 'class (\w+)') { }
    $report.services += $_.Name
}

# --- Config gates ------------------------------------------------------------------
$appSettings = Join-Path $DemoRoot 'appsettings.json'
if (Test-Path $appSettings) {
    try {
        $cfg = Get-Content $appSettings -Raw | ConvertFrom-Json
        if ($cfg.DemoSecurity) {
            $report.configGates += , [ordered]@{
                requireProviderInProduction = $cfg.DemoSecurity.RequireProviderInProduction
                allowOllamaInProduction     = $cfg.DemoSecurity.AllowOllamaInProduction
                rateLimitingEnabled         = $cfg.DemoSecurity.RateLimiting.Enabled
                dailyCostLimitUsd           = $cfg.DemoLogging.DailyCostLimitUsd
                remoteStorageAdapter        = $cfg.DemoRemoteStorage.Adapter
            }
        }
    } catch { Write-Warning "Could not parse appsettings.json: $_" }
}

$json = $report | ConvertTo-Json -Depth 6

if ($OutputPath) {
    $json | Set-Content -Path $OutputPath -Encoding UTF8
    Write-Host "Audit written to $OutputPath"
} else {
    $json
}
