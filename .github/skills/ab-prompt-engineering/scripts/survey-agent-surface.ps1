<#
.SYNOPSIS
    Surveys a consumer app's SourceDir for AgentBlazor capability, action, param,
    component, and tool registrations and emits a machine-readable register.

.DESCRIPTION
    Scans C# source under -SourceDir for the registrations that drive an
    AgentBlazor agent's surface area:
      - [AgentCapability] classes and their [AgentAction] methods
      - [AgentParam] on action parameters
      - [AgentComponent] / [AgentReadable] component declarations
      - AddTool(...) service-tool registrations
    The emitted register (JSON by default) is the evidence baseline used by the
    ab-prompt-engineering alignment workflow to keep a system prompt in sync with
    what the agent can actually do.

.PARAMETER SourceDir
    Root directory to scan for C# source files. Required.

.PARAMETER OutFile
    Path to write the register. Defaults to "agent-surface.json".

.PARAMETER Format
    Output format: Json | Csv | Table. Defaults to Json.

.PARAMETER ToolNames
    Optional comma-separated list of well-known tool names to recognize AddTool("name", ...)
    calls. When omitted, the script falls back to parsing the first string literal argument.

.EXAMPLE
    .\survey-agent-surface.ps1 -SourceDir ./Components -OutFile agent-surface.json
.EXAMPLE
    .\survey-agent-surface.ps1 -SourceDir ./Features -Format Csv
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string]$SourceDir,

    [Parameter(Mandatory = $false)]
    [string]$OutFile = "agent-surface.json",

    [ValidateSet("Json", "Csv", "Table")]
    [string]$Format = "Json",

    [string]$ToolNames = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SourceFiles {
    param([string]$Dir)

    if (-not (Test-Path -LiteralPath $Dir)) {
        throw "Source directory not found: $Dir"
    }

    Get-ChildItem -LiteralPath $Dir -Recurse -Include *.cs -File -ErrorAction SilentlyContinue
}

function Get-AttributeBody {
    # Returns the text between the matching brackets of an "[AttributeName(... )]" occurrence.
    param([string]$Text, [int]$StartIndex)

    $open = $Text.IndexOf('(', $StartIndex)
    if ($open -lt 0) { return '' }

    $close = Get-ClosingParen -Text $Text -OpenIndex $open
    if ($close -lt 0) { return '' }
    return $Text.Substring($open + 1, $close - $open - 1)
}

function Get-ClosingParen {
    # Returns the index of the ')' that balances the '(' at OpenIndex, ignoring nested parens.
    param([string]$Text, [int]$OpenIndex)

    $depth = 0
    for ($i = $OpenIndex; $i -lt $Text.Length; $i++) {
        if ($Text[$i] -eq '(') { $depth++ }
        elseif ($Text[$i] -eq ')') {
            $depth--
            if ($depth -eq 0) { return $i }
        }
    }
    return -1
}

function Get-ClosingBrace {
    # Returns the index of the '}' that balances the '{' at OpenIndex, ignoring nested braces.
    param([string]$Text, [int]$OpenIndex)

    $depth = 0
    for ($i = $OpenIndex; $i -lt $Text.Length; $i++) {
        if ($Text[$i] -eq '{') { $depth++ }
        elseif ($Text[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $i }
        }
    }
    return -1
}

function Get-AttributeValue {
    # Pulls "key = value" or positional value out of an attribute body.
    param([string]$Body, [string]$Key)

    $Body = $Body.Trim()
    if ($Key) {
        # quoted key="..." or key = "value"; also unquoted key with ) or , delimiter
        $pattern = [regex]"(?i)\b$([regex]::Escape($Key))\b\s*=\s*(?:""(?<q>[^""]*)""|'(?<s>[^']*)'|(?<r>[^\s,\)]+))"
        $m = $pattern.Match($Body)
        if ($m.Success) {
            if ($m.Groups['q'].Success) { return $m.Groups['q'].Value }
            if ($m.Groups['s'].Success) { return $m.Groups['s'].Value }
            return $m.Groups['r'].Value
        }
        return ''
    }

    # Positional first string literal
    $pos = [regex]'^\s*"(?<v>[^"]*)"|^\s*''(?<v>[^'']*)'''
    $pm = $pos.Match($Body)
    if ($pm.Success) { return $pm.Groups['v'].Value }
    return ''
}

function Get-ActionParams {
    param([string]$MethodBody)

    $params = @()
    # matches parameter attribute sets like [AgentParam(Description="...", Required=true)] and bare [AgentParam]
    $re = [regex]'\[AgentParam\((?<body>.*?)\)\]|\[AgentParam\](?<empty>\s)'
    foreach ($m in $re.Matches($MethodBody)) {
        $body = $m.Groups['body'].Value
        $desc = Get-AttributeValue -Body $body -Key 'Description'
        if (-not $desc) { $desc = Get-AttributeValue -Body $body -Key '' }
        $params += [pscustomobject]@{
            Name            = ''
            Description     = $desc
            Required        = (Get-AttributeValue -Body $body -Key 'Required')
            AllowedValues   = (Get-AttributeValue -Body $body -Key 'AllowedValues')
            ContextKey      = (Get-AttributeValue -Body $body -Key 'ContextKey')
        }
    }
    return $params
}

function Get-SnakeCase {
    param([string]$Name)

    if (-not $Name) { return $Name }
    $snake = ($Name -creplace '([a-z0-9])([A-Z])', '$1_$2' -creplace '([A-Z]+)([A-Z][a-z])', '$1_$2').ToLowerInvariant()
    return $snake
}

function Find-ToolName {
    # Recognizes AddTool(@"literal", ...) or AddTool("literal", ...).
    param([string]$CallText)

    $named = $CallText | Select-String -Pattern 'AddTool\(\s*(?:@"|")(?<name>[^"]+)"' -AllMatches
    if ($named.Matches.Count -gt 0) { return $named.Matches[0].Groups['name'].Value }
    return ''
}

$files = Get-SourceFiles -Dir $SourceDir
if ($files.Count -eq 0) {
    Write-Warning "No C# files found under '$SourceDir'. Emitting an empty register."
}

$capabilities = New-Object System.Collections.Generic.List[object]
$components   = New-Object System.Collections.Generic.List[object]
$tools        = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop

    # --- Capabilities & actions ---
    $capRegex = [regex]'\[AgentCapability(?<capbody>[^\]]*)\]\s*(?:public\s+|internal\s+|sealed\s+|abstract\s+|static\s+|partial\s+)*class\s+(?<class>\w+)'
    foreach ($cm in $capRegex.Matches($text)) {
        $capId = Get-AttributeValue -Body $cm.Groups['capbody'].Value -Key 'CapabilityId'
        if (-not $capId) { $capId = Get-SnakeCase -Name $cm.Groups['class'].Value }
        $capName = Get-AttributeValue -Body $cm.Groups['capbody'].Value -Key 'Name'
        $capDesc = Get-AttributeValue -Body $cm.Groups['capbody'].Value -Key 'Description'

        # find the class body to scope action matches (balanced brace extraction)
        $classIdx = $cm.Groups['class'].Index
        $openBrace = $text.IndexOf('{', $classIdx)
        if ($openBrace -lt 0) { continue }
        $closeBrace = Get-ClosingBrace -Text $text -OpenIndex $openBrace
        if ($closeBrace -lt 0) { $closeBrace = $text.Length }
        $classBody = $text.Substring($openBrace, $closeBrace - $openBrace)

        $actRegex = [regex]'\[AgentAction\((?<actbody>[^\]]*)\)\]\s*(?:public\s+)?[\w\.<>]+\s+(?<method>\w+)\s*\('
        foreach ($am in $actRegex.Matches($classBody)) {
            $desc = Get-AttributeValue -Body $am.Groups['actbody'].Value -Key 'Description'
            if (-not $desc) { $desc = Get-AttributeValue -Body $am.Groups['actbody'].Value -Key '' }
            $actionId = Get-AttributeValue -Body $am.Groups['actbody'].Value -Key 'ActionId'
            if (-not $actionId) { $actionId = Get-SnakeCase -Name $am.Groups['method'].Value }
            $requiresApproval = (Get-AttributeValue -Body $am.Groups['actbody'].Value -Key 'RequiresApproval')
            $followUp        = (Get-AttributeValue -Body $am.Groups['actbody'].Value -Key 'FollowUp')
            $instructions    = (Get-AttributeValue -Body $am.Groups['actbody'].Value -Key 'Instructions')

            # scope to the method body for [AgentParam]
            $methodIdx = $am.Groups['method'].Index
            $mOpen = $classBody.IndexOf('(', $methodIdx)
            $mClose = Get-ClosingParen -Text $classBody -OpenIndex $mOpen
            if ($mClose -lt 0) { continue }
            $methodHead = $classBody.Substring($methodIdx, $mClose - $methodIdx + 1)

            $capabilities.Add([pscustomobject]@{
                CapabilityId     = $capId
                CapabilityName   = $capName
                CapabilityDesc   = $capDesc
                ActionId         = $actionId
                ActionDesc       = $desc
                RequiresApproval = $requiresApproval
                FollowUp         = $followUp
                Instructions     = $instructions
                Parameters       = @(Get-ActionParams -MethodBody $methodHead)
                File             = $file.Name
            })
        }
    }

    # --- Components (custom [AgentComponent] classes OR IAgentControllable impls) ---
    $compRegex = [regex]'\[AgentComponent(?<compbody>[^\]]*)\]\s*(?:public\s+|internal\s+|sealed\s+|abstract\s+|static\s+|partial\s+)*class\s+(?<name>\w+)'
    $seenCompIds = @{}
    foreach ($c in $compRegex.Matches($text)) {
        $componentId = Get-AttributeValue -Body $c.Groups['compbody'].Value -Key ''
        if (-not $componentId) { $componentId = $c.Groups['name'].Value }
        if ($seenCompIds[$componentId]) { continue }
        $seenCompIds[$componentId] = $true
        $components.Add([pscustomobject]@{
            ComponentId = $componentId
            ComponentName = $c.Groups['name'].Value
            File = $file.Name
        })
    }
    # Fallback: classes that implement IAgentControllable
    $controllableRegex = [regex]("class\s+(?<name>\w+)\s*:[^\{]*,?\s*IAgentControllable\b")
    foreach ($c in $controllableRegex.Matches($text)) {
        $componentId = $c.Groups['name'].Value
        if ($seenCompIds[$componentId]) { continue }
        $seenCompIds[$componentId] = $true
        $components.Add([pscustomobject]@{
            ComponentId = $componentId
            ComponentName = $c.Groups['name'].Value
            File = $file.Name
        })
    }

    # --- Tools (AddTool registrations) ---
    $toolRegex = [regex]'AddTool\(\s*(?<args>.{0,500}?)\)'
    foreach ($t in $toolRegex.Matches($text)) {
        $name = Find-ToolName -CallText $t.Value
        if ($name) {
            $tools.Add([pscustomobject]@{
                Name = $name
                File = $file.Name
            })
        }
    }
}

$register = [pscustomobject]@{
    GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    SourceDir      = (Resolve-Path -LiteralPath $SourceDir).Path
    Capabilities   = $capabilities
    Components     = $components
    Tools          = $tools
}

switch ($Format) {
    'Json' {
        $json = $register | ConvertTo-Json -Depth 6
        $json | Set-Content -LiteralPath $OutFile -Encoding utf8
        Write-Output $json
    }
    'Csv' {
        $allActions = foreach ($cap in $capabilities) {
            [pscustomobject]@{
                CapabilityId     = $cap.CapabilityId
                ActionId         = $cap.ActionId
                ActionDesc       = $cap.ActionDesc
                RequiresApproval = $cap.RequiresApproval
                FollowUp         = $cap.FollowUp
            }
        }
        $allActions | Export-Csv -LiteralPath $OutFile -NoTypeInformation
        $allActions | Format-Table -AutoSize
    }
    'Table' {
        Write-Output "Capabilities / Actions:"
        $capabilities | Format-Table -AutoSize CapabilityId, ActionId, RequiresApproval, File
        Write-Output "Components:"
        $components | Format-Table -AutoSize ComponentId, ComponentName, File
        Write-Output "Tools:"
        $tools | Format-Table -AutoSize Name, File
    }
}