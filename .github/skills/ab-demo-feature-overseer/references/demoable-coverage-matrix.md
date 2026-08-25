# Demoable Features Coverage Matrix (Living Doc)

The **single source of truth** for which AgentBlazor feature is actually implemented in
the **AgentBlazor.Demo** project vs. which is only demoable (library-supported) but NOT
yet shown in the Demo.

- **Left column** = the candidate demoable features (the broad surface AgentBlazor can
  demo). Kept aligned with the owning ab\* skills.
- **Coverage** = what the Demo actually implements, backed by audit evidence. Do NOT
  mark `implemented` without current source evidence.

**Evidence** is refreshed by running the probe script and spot-checking the areas it
doesn't cover:

```powershell
pwsh .github/skills/ab-demo-feature-overseer/scripts/audit-demo-features.ps1 -OutputPath artifacts/demo-audit.json
```

Status legend: ✅ implemented · 🟡 wired-but-dormant (code present, inactive) ·
🔶 partial · ⛔ not-demoed (no concrete demo use case wired in the Demo).

**Grounding rule:** a row counts as ✅/🔶 only if there is a **corresponding demo use
case actually wired in the Demo project** — not just because the library supports it
or because some parameter default is active. If the Demo never exercises the feature,
mark it ⛔.

_Evidence snapshot: 2026-08-25 (8 workflow agents, 3 standalone agents, 8 capability
classes, 41 agent actions, 10 approvals, 2 clarification sites, 17 component families,
25 routes, 9 scenarios, 2 middleware, 8 log endpoints)._

---

## 1. Agents & registration

| Demoable feature | Coverage | Where (evidence) | ab\* skill |
|---|---|---|---|
| Standalone agent registration | ✅ | `agentRegistrations[3]` → Workflow Hub, Supplier Analyst, Workflow Orchestrator | `ab-agent-registration` |
| Workflow-capability agent registration | ✅ | `agentRegistrations[8]`, `workflows[8]` | `ab-agent-registration` |
| Route prefixes per agent | ✅ | `agentRegistrations[].routePrefixes` | `ab-agent-registration` |
| Allowed components per agent | ✅ | `agentRegistrations[].allowedComponents` | `ab-agent-registration` |
| Shared instructions file | ✅ | `agent-instructions.txt` + `hasSharedInstructions=true` (10 of 11) | `ab-context-assembly` |
| Semantic data schemas (`AgentDataSchemaSet`) | ✅ | `dataSchemas.schemaSets=[support-data]`, bound to Support Inbox | `ab-agent-registration` |
| Allowed **actions** / capability actions (fine-grained) | ⛔ | no `WithAllowedActions` in Demo | `ab-agent-registration` |
| Service **tools** (`AddTool`) | ⛔ | no `AddTool`/`AgentServiceTool` | `ab-tool-registration` |
| **MCP** server tools (`UseMcpServer`) | ⛔ | no MCP wiring | `ab-tool-registration` |
| Agent **selector** in chat | ⛔ | chat surfaces are locked to a per-route `DefaultAgentName` (`LockAgentToCurrentRoute`); `ShowAgentSelector` only toggles pane-vs-widget, never a user-visible multi-agent picker | `ab-in-chat-features` |

## 2. Capabilities & actions

| Demoable feature | Coverage | Where (evidence) | ab\* skill |
|---|---|---|---|
| `[AgentCapability]` classes | ✅ | 8 capability classes (incl. 4 in `*WorkflowService.cs`) | `ab-capability-authoring` |
| `[AgentAction]` typed actions | ✅ | 41 actions across 8 classes | `ab-capability-authoring` |
| Approval boundaries (`RequiresApproval`) | ✅ | 10 approvals (draft/escalation/handoff/remediation) | `ab-capability-authoring`, `ab-in-chat-features` |
| Clarification (`NeedsClarification`) | ✅ | SupplierCompliance(1), SupportInbox(2) | `ab-in-chat-features` |
| Structured outputs / next actions (`WithOutput`, `WithNextAction`) | ✅ | RuntimeProbe structured-error probe; workflows | `ab-capability-authoring` |
| Recovery playbook + reset actions | ✅ | every workflow has `apply_*_recovery_playbook` + `reset_*` | `ab-capability-authoring` |
| `[AgentParam]` richer param contracts | ⛔ | no `[AgentParam]` attribute in Demo; only plain action method args | `ab-capability-authoring` |
| Handoff approval policy (`RequireHandoffApproval`) | ⛔ | no handoff-approval wiring | `ab-in-chat-features` |

## 3. Chat & conversation

| Demoable feature | Coverage | Where (evidence) | ab\* skill |
|---|---|---|---|
| Embedded chat surface (`AgentChatSurface`) | ✅ | `DemoLayout` | `ab-chat-composer` |
| Floating chat widget (`AgentChatWidget`) | ✅ | `DemoLayout` | `ab-chat-composer` |
| Generated-UI cards (`EnableGeneratedUi`) | ✅ | `EnableGeneratedUi=true` on both surfaces | `ab-in-chat-features` |
| Session key isolation | ✅ | `SessionId` from `ComponentRegistry.SessionId` | `ab-chat-session-management` |
| Conversation persistence (durable store) | 🔶 | default store (InMemory); `agentblazor-demo.db` present; no `Use*ConversationStore` override | `ab-conversation-store` |
| Session browser / history resume | ⛔ | no session-browser component | `ab-chat-session-management` |
| Suggestion chips / predefined prompts | ⛔ | no `PredefinedPrompts`/`Suggestions` params | `ab-in-chat-features` |
| Proactive insights | ⛔ | no proactive-insight wiring; only host-side workflow "insight" panels | `ab-in-chat-features` |
| Slash commands | ⛔ | no slash-command surface | `ab-in-chat-features` |
| Stop button / timeout warning / error boundary | ⛔ | only library defaults on chat surfaces; no demo use case exercises them | `ab-in-chat-features` |

## 4. Agent-controllable components (MudBlazor)

| Demoable feature | Coverage | Where (evidence) | ab\* skill |
|---|---|---|---|
| `AgentDataGrid` | ✅ | Components, SupplierCompliance, SupportInbox, RecipeRelease | `ab-mud-components` |
| `AgentForm` | ✅ | Components, RecipeRelease | `ab-mud-components` |
| `AgentDialog` | ✅ | Components + 5 workflows | `ab-mud-components` |
| `AgentSelect` | ✅ | Components | `ab-mud-components` |
| `AgentAutocomplete` | ✅ | Components | `ab-mud-components` |
| `AgentDatePicker` | ✅ | Components | `ab-mud-components` |
| `AgentDateRangePicker` | ✅ | Components | `ab-mud-components` |
| `AgentTreeView` | ✅ | Components, IncidentEscalation | `ab-mud-components` |
| `AgentStepper` | ✅ | Components, IncidentEscalation | `ab-mud-components` |
| `AgentTabs` | ✅ | Components, IncidentEscalation | `ab-mud-components` |
| `AgentCommandBar` | ✅ | Components, FileAuditBundle, IncidentEscalation | `ab-mud-components` |
| `AgentFileUpload` | ✅ | Components, FileAuditBundle | `ab-mud-components` |
| `AgentNavMenu` | ✅ | Components | `ab-mud-components` |
| `AgentMarkdownContent` | ✅ | MarkdownShowcase, docs | `ab-mud-components` |
| `AgentProDashboard` | ✅ | ProDashboard (`/demo/dashboard`) | `ab-mud-components` |
| `AgentGenerativeSurface` / `AgentGeneratedCard/Form/Chart/Table` | ⛔ | not used directly | `ab-mud-components` |

## 5. Inspector & observability

| Demoable feature | Coverage | Where (evidence) | ab\* skill |
|---|---|---|---|
| Request-logging middleware | ✅ | `UseMiddleware<DemoChatRequestLoggingMiddleware>` | `ab-middleware-authoring` |
| Traffic-logging middleware | ✅ | `app.UseMiddleware<DemoTrafficLoggingMiddleware>` | `ab-middleware-authoring` |
| Log inspection endpoints | ✅ | `/demo-log/{login,logout,view,/,traffic,summary,download,traffic/download}` | `ab-middleware-authoring` |
| Prompt tracing | ✅ | `agentBuilder.EnablePromptTracing()` | `ab-context-assembly`, `ab-inspector` |
| Dev tools / Agent Inspector panel | 🟡 | `UseDevTools()` commented out (`providers.devTools=false`); Pro license can enable durable inspector store | `ab-inspector` |
| `AgentInspectorPanel` explicit embedding | ⛔ | not embedded directly | `ab-inspector` |

## 6. Provider & runtime

| Demoable feature | Coverage | Where (evidence) | ab\* skill |
|---|---|---|---|
| OpenAI provider | ✅ | `UseOpenAI(apiKey, gpt-4o-mini)` | `ab-provider-config` |
| Ollama provider | ✅ | `UseOllama(...)` | `ab-provider-config` |
| Pro license (durable SQLite stores) | 🟡 | `UseProLicense(...)` gated on unset key | `ab-conversation-store`, `ab-inspector` |
| Custom `ConfigureChatOptions` pinning | ⛔ | no `ConfigureChatOptions` in Demo | `ab-provider-config` |
| Runtime probes (cancel/reconnect/approval) | ✅ | RuntimeProbe workflow + capability | `ab-in-chat-features`, `ab-testing` |
| Multi-tenant deploy (Finbuckle) | ⛔ | Demo is single-tenant | `ab-multitenancy` |
| UI-library coexistence (CSS isolation) | ⛔ | Demo is MudBlazor-native; coexistence with another UI library is never exercised | `ab-ui-integration` |

## 7. Workflow domain / persistence / misc

| Demoable feature | Coverage | Where (evidence) | ab\* skill |
|---|---|---|---|
| EF Core domain entities | ✅ | `Data/*.cs` (Dojo, File workflow, DbContext) | `ab-entity-design` |
| SQLite DB seeder | ✅ | `DemoWorkflowDatabaseSeeder` | `ab-entity-design` |
| Remote-storage handoff adapter | 🔶 | `DemoRemoteStorageAdapter` (InMemory default; HTTP adapter configurable) | `ab-capability-authoring` |
| Rate limiting / security gates | ✅ | `DemoSecurityOptions`, `AddRateLimiter` on agent endpoints | `ab-testing`, `ab-middleware-authoring` |
| Demo workflow domain services | ✅ | 25 service files | `ab-entity-design`, `ab-conversation-store` |

---

## Coverage summary

| Area | ✅ | 🟡 | 🔶 | ⛔ |
|---|---|---|---|---|
| Agents & registration | 6 | 0 | 0 | 4 |
| Capabilities & actions | 6 | 0 | 0 | 2 |
| Chat & conversation | 4 | 0 | 1 | 5 |
| MudBlazor components | 15 | 0 | 0 | 1 |
| Inspector & observability | 5 | 1 | 0 | 1 |
| Provider & runtime | 5 | 1 | 0 | 2 |
| Workflow/misc | 4 | 1 | 1 | 0 |

**Not-demoed leaders worth adding next** (⛔ ≥1): service/MCP tools, fine-grained
`WithAllowedActions`, session browser, suggestion chips / proactive insights / slash
commands, agent selector, `[AgentParam]`, generative-UI blocks
(`AgentGenerativeSurface`), `ConfigureChatOptions` pinning, multitenancy, and UI-library
coexistence.

---

## Keeping this matrix honest

1. **Refresh evidence first**: run the audit script, then spot-check the 🟡/🔶/⛔ rows by
   searching the Demo source for the wiring token (e.g. `AddTool`, `PredefinedPrompts`,
   `UseDevTools`).
2. **Update status only on evidence.** If a feature is not scanned by the script, grep
   the Demo manually and record the finding.
3. **When adding a Demo feature**, mark it here and record its wiring + owning ab\* skill.
4. **Keep per-area counts** (`## Coverage summary`) in sync with the rows above.