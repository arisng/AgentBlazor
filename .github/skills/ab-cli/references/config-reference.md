# Configuration Reference

## `.agentblazorc` file

JSON at the solution root (searched up the directory tree), camelCase keys, created by `agentblazor init --save-config`. Read by `analyze`, `scaffold workflows`, and `watch`. Invalid or unreadable files are treated as no config.

| Key | Type | Purpose |
|---|---|---|
| `hostProject` | string | Blazor host project name (fallback when `--host` omitted) |
| `description` | string | Short app description for the AGENT.md header |
| `desiredAgentWorkflows` | string[] | Intent hints for workflow onboarding (not proof the app supports them) |
| `watchDebounceMs` | int | Watch mode debounce, default 500 |
| `additionalServiceSuffixes` | string[] | Extra service class suffixes to detect (e.g. `["Interactor", "UseCase"]`) |
| `additionalDomainVerbs` | string[] | Extra domain verbs recognized as actions (e.g. `["Claim", "Release"]`) |
| `excludeMethodPatterns` | string[] | Method name patterns excluded from action detection |
| `excludeServicePatterns` | string[] | Service name patterns excluded from analysis |
| `excludeDirectories` | string[] | Directories excluded from scanning (relative to host project) |
| `autoUpdateOnBuild` | bool | Whether the MSBuild target runs `update` after build (default true) |
| `analyzeProvider` | string | `openai` or `azure-openai` for `analyze` |
| `analyzeModel` | string | Model name for `analyze` |

Never store API keys in this file — supply them through environment variables.

## Environment variables

| Variable | Used by | Notes |
|---|---|---|
| `OPENAI_API_KEY` or `OpenAI__ApiKey` | `analyze` | Provider key; prompts interactively when missing |
| `AGENTBLAZOR_ANALYZE_MODEL`, `OPENAI_MODEL`, or `OpenAI__Model` | `analyze` | Model override |
| `AGENTBLAZOR_ANALYZE_PROVIDER=azure-openai` | `analyze` | Switches to Azure OpenAI |
| `AZURE_OPENAI_ENDPOINT` | `analyze` | Azure resource endpoint |
| `AZURE_OPENAI_DEPLOYMENT` | `analyze` | Azure deployment name |
| `AZURE_OPENAI_API_KEY` | `analyze` | Azure key |
| `AGENTBLAZOR_STATIC_WORKSPACE=1` | `analyze` | Force static source-file analysis |
| `AGENTBLAZOR_DEBUG=1` | `doctor`, `validate` | Print full exception details |

When no provider is configured and the terminal is non-interactive, `analyze` exits before scanning and reports which variables to set.

## Provider examples

OpenAI (repeat runs / CI):

```bash
export OPENAI_API_KEY="<key>"
export AGENTBLAZOR_ANALYZE_MODEL="gpt-4o-mini"
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

PowerShell:

```powershell
$env:OPENAI_API_KEY = "<key>"
$env:AGENTBLAZOR_ANALYZE_MODEL = "gpt-4o-mini"
agentblazor analyze .\MySolution.slnx --host MyBlazorApp
```

Azure OpenAI:

```bash
export AGENTBLAZOR_ANALYZE_PROVIDER="azure-openai"
export AZURE_OPENAI_ENDPOINT="https://<resource>.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT="<deployment-name>"
export AZURE_OPENAI_API_KEY="<azure-key>"
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

Avoid the LLM entirely: `agentblazor analyze ./MySolution.slnx --host MyBlazorApp --static-only`.

> **Note (provider options after scaffold):** `agentblazor scaffold --provider openai` writes a baseline `options.UseOpenAI(apiKey, model)` block in `Program.cs`. That block is a plain provider registration — it does **not** include provider-level `ChatOptions` configuration. If your model family needs one (e.g. pinning `ReasoningEffort.None` for gpt-5.6-family tools), extend the scaffolded block manually with `options.ConfigureChatOptions(...)` (AgentBlazor 0.2.23+) — see the **`ab-provider-config` skill**. Scaffolding is append-only/non-destructive, so re-running `scaffold` after your manual edit is safe and inert.
