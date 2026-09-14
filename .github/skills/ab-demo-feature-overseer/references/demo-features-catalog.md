# Demo Features Catalog — AgentBlazor.Demo

Maintained empirical audit of demoable features in `demo/AgentBlazor.Demo`. Status is
evidence-gated — see `catalog-schema.md` for the taxonomy. Regenerate evidence with
`.github/skills/ab-demo-feature-overseer/scripts/audit-demo-features.ps1` before editing.

Last audited: 2026-09-14 (audit.json: 9 workflow agents, 3 standalone agents, sourced from `DemoAgentDatabaseSeeder.BuildSeeds()`;
9 capability classes, 44 agent actions, 11 approvals, 3 clarification sites,
18 component page families, 30 routes, 9 launchpad scenarios; runtime
customization seam wired; **Agent Builder dynamic-registry showcase wired**)._

## Agents

### Agent Builder — database-backed dynamic registry
- **Status**: implemented
- **What it demonstrates**: building + registering agents **at runtime** against a
  database-backed `IAgentRegistry` (replace path) — create/edit/delete agents, edit
  persona + enabled tool set, chat with a just-built agent. The dynamic counterpart
  to static `AddAgent`/`AddWorkflow`.
- **Where**: `Program.cs` → `AddSingleton<DatabaseBackedAgentRegistry>()` +
  `AddSingleton<IAgentRegistry>(...)` BEFORE `AddAgentBlazor`; page
  `Components/Pages/Demo/AgentBuilder.razor` (`/demo/agent-builder`); services
  `DatabaseBackedAgentRegistry.cs`, `DemoAgentDatabaseSeeder.cs`; entity
  `Data/AgentDefinitionEntity.cs` + `Data/DemoAgentDbContext.cs`.
- **Audit evidence**: page route `@page "/demo/agent-builder"` in `routes[]`;
  `DatabaseBackedAgentRegistry` implements `IAgentRegistry`; `DemoAgentDbContext`.
- **ab\* skill**: `ab-agent-registration` (Dynamic Agent Registration),
  `ab-context-assembly` (customizer integration), `ab-entity-design`.

### Workflow-capability agents
- **Status**: implemented (9)
- **What it demonstrates**: semantic workflow agents routing to their showcase route.
- **Where**: capability classes registered via `AddCapability<T>` in `Program.cs`;
  agent definitions seeded into the database registry by `DemoAgentDatabaseSeeder`.
- **Audit evidence**: `workflows[]` — Supplier Compliance, Support Inbox, File
  Workflow, Recipe Release, Incident Escalation, Response Orchestration, Release
  Dossier, Runtime Probe, Customization Demo.
- **ab\* skill**: `ab-agent-registration`, `ab-capability-authoring`.

### Standalone agents
- **Status**: implemented (3)
- **What it demonstrates**: non-workflow agents with route/component scope.
- **Where**: seeded into the database registry by `DemoAgentDatabaseSeeder.BuildSeeds()`.
- **Audit evidence**: `agents[]` — Workflow Hub, Supplier Analyst, Workflow
  Orchestrator.
- **ab\* skill**: `ab-agent-registration`.

### Shared instructions & route/component scope
- **Status**: implemented
- **Where**: `agent-instructions.txt` → passed to `DemoAgentDatabaseSeeder`;
  agents seeded with `Instructions` + `Metadata["route_prefixes"]` + `AllowedComponents`.
- **Audit evidence**: `agentRegistrations[].hasSharedInstructions`; per-agent
  `allowedComponents[]` and `routePrefixes`.
- **ab\* skill**: `ab-context-assembly`, `ab-agent-registration`.

### Semantic data schemas
- **Status**: implemented
- **What**: `support-data` `AgentDataSchemaSet` bound to the Support Inbox agent.
- **Audit evidence**: `dataSchemas.schemaSets = [support-data]`;
  `agentRegistrations[Support Inbox Agent].dataSchemas = [support-data]`.
- **ab\* skill**: `ab-agent-registration`.

## Capabilities & actions

### Capability classes
- **Status**: implemented (9)
- **Where**: `Services/*Capabilities*.cs` and `*WorkflowService.cs`.
- **Audit evidence**: `capabilities[]` with ids `file_audit_bundle`, `recipe_release`,
  `incident_escalation`, `release_dossier`, `response_orchestration`, `runtime_probe`,
  `supplier_compliance`, `support_inbox`, `customization_demo`.
- **ab\* skill**: `ab-capability-authoring`.

### Typed agent actions
- **Status**: implemented (44 total across 9 classes)
- **Audit evidence**: `capabilities[].actions[]` (each `[AgentAction(id, description)]`).
- **ab\* skill**: `ab-capability-authoring`.

### Approval boundaries
- **Status**: implemented (11)
- **What**: destructive/mutating actions require human approval before executing.
- **Audit evidence**: `approvals[]` — `prepare_audit_bundle`, `prepare_release_draft`,
  `prepare_escalation_brief`, `submit_escalation_handoff`, `prepare_release_dossier`,
  `prepare_response_packet`, `run_approval_probe`, `prepare_remediation_draft`,
  `draft_ticket_reply_for_ticket`, `draft_ticket_reply`, `run_quick_check`.
- **ab\* skill**: `ab-in-chat-features`, `ab-capability-authoring`.

### Clarification requests
- **Status**: implemented (3 sites)
- **What**: agents ask which tickets/suppliers to act on when the target is ambiguous.
- **Audit evidence**: `clarifications[]` — `SupplierComplianceWorkflowService:1`,
  `SupportInboxWorkflowService:2`.
- **ab\* skill**: `ab-in-chat-features`.

### Structured outputs & next actions
- **Status**: implemented
- **What**: `WithOutput` structured results and `WithNextAction` guidance (e.g.
  `RuntimeProbeCapabilities` structured-error date-range probe).
- **ab\* skill**: `ab-capability-authoring`.

### Recovery playbook + reset actions
- **Status**: implemented
- **What**: `apply_*_recovery_playbook` / `reset_*` actions in every workflow.
- **ab\* skill**: `ab-capability-authoring`.

## Runtime customization

### Per-agent instructions + tool filtering (`IAgentRuntimeCustomizer`)
- **Status**: implemented
- **What**: the `Customization Demo Agent`'s persona (system instructions) and tool set
  are edited live on `/demo/customization`; the `DemoAgentCustomizer` reads the
  `DemoAgentCustomizationStore` per turn and returns `null` for unconfigured agents
  (standard agents unaffected). One action (`run_quick_check`) is approval-gated to show
  customization + approval interplay.
- **Where**: `Program.cs` → `AddRuntimeCustomizer<DemoAgentCustomizer>` +
  `AddWorkflow<CustomizationDemoCapabilities>("Customization Demo Agent")`;
  `Services/DemoAgentCustomizationStore.cs`, `Services/DemoAgentCustomizer.cs`,
  `Services/CustomizationDemoCapabilities.cs`; page `CustomizationShowcase.razor`.
- **Audit evidence**: `runtimeCustomization.customizerRegistered = true`,
  `customizerTypes = [DemoAgentCustomizer]`.
- **ab\* skill**: `ab-context-assembly` (per-agent instructions), `ab-tool-authoring`
  (logical-id tool filtering).

## Chat & conversation

### Embedded chat surface
- **Status**: implemented
- **Where**: `DemoLayout` → `AgentChatSurface` (split assistant pane).
- **Audit evidence**: `components[] = AgentChatSurface`.
- **ab\* skill**: `ab-chat-composer`.

### Floating chat widget
- **Status**: implemented
- **Where**: `DemoLayout` → `AgentChatWidget`.
- **Audit evidence**: `components[] = AgentChatWidget`.
- **ab\* skill**: `ab-chat-composer`.

### Generated-UI rendering (cards)
- **Status**: implemented
- **Where**: `DemoLayout` `EnableGeneratedUi="true"` on both surfaces.
- **ab\* skill**: `ab-in-chat-features`.

### Session key isolation
- **Status**: implemented
- **Where**: pages use `ComponentRegistry.SessionId`; chat surfaces pass `SessionId`.
- **ab\* skill**: `ab-chat-session-management`.

### Session browser / history resume
- **Status**: implemented
- **What**: two-column master-detail on `/demo/sessions` — session list left,
  `AgentChatSurface` timeline + composer right; New-chat button with agent picker
  (registry-sourced, `demo:{guid}:{route}` draft IDs promoted on first send);
  resume and new surfaces use `SessionId=base` + `DefaultAgentName` with
  `LockAgentToCurrentRoute="false"`, no `LockedAgentName`, `EnableAgentHandoff="false"`;
  `?session=` deep links and agent→route affinity chips.
- **Where**: `Components/Pages/Demo/SessionBrowser.razor` (+ `.razor.css`),
  `Services/DemoSessionBrowserService.cs` (`GetAvailableAgents`, `BuildNewBaseSessionId`,
  `GetRouteForAgent`, key split, real `LastActivityAt`),
  `Components/Layout/DemoLayout.razor` (widget suppressed on `/demo/sessions`).
- **Audit evidence**: `SessionBrowser.razor` hosts `AgentChatSurface` with
  `DefaultAgentName` (no `LockedAgentName`); `DemoSessionBrowserService` calls
  `GetActiveSessionsAsync` + `GetHistoryAsync` + `IAgentRegistry.GetAll()`;
  `DemoLayout` gates `ShowAssistantWidget` on `IsSessionBrowserRoute`.
- **ab\* skill**: `ab-chat-session-management`.

### Conversation persistence
- **Status**: implemented — durable store (JSON-file by default; SQLite EF Core when `DemoConversation.Store=EFCore`; InMemory only when explicitly set).
- **Where**: `Program.cs` ConfigureBuilder → `UseJsonFileConversationStore(...)` (default) or `UseConversationStore(sp => new DemoConversationStore(...))` when `Store=EFCore`; selector `Configuration/DemoConversationOptions.cs` (`Store` default `"JsonFile"`, overridden to `"EFCore"` in `appsettings.json`).
- **Audit evidence**: `UseJsonFileConversationStore` / `UseConversationStore(DemoConversationStore)` calls in demo source; `demo_conversation_sessions`/`demo_conversation_turns` tables.
- **ab\* skill**: `ab-conversation-store`.

## Agent-controllable components (MudBlazor)

All statuses `implemented`; pages per `components[].files`.

| Component | Page(s) | ab\* skill |
|-----------|---------|------------|
| `AgentDataGrid` | `Components`, `SupplierCompliance`, `SupportInbox`, `RecipeRelease` | `ab-mud-components` |
| `AgentForm` | `Components`, `RecipeRelease` | `ab-mud-components` |
| `AgentDialog` | `Components`, 5+ workflows | `ab-mud-components` |
| `AgentSelect` | `Components` | `ab-mud-components` |
| `AgentAutocomplete` | `Components` | `ab-mud-components` |
| `AgentDatePicker` | `Components` | `ab-mud-components` |
| `AgentDateRangePicker` | `Components` | `ab-mud-components` |
| `AgentTreeView` | `Components`, `IncidentEscalation` | `ab-mud-components` |
| `AgentStepper` | `Components`, `IncidentEscalation` | `ab-mud-components` |
| `AgentTabs` | `Components`, `IncidentEscalation` | `ab-mud-components` |
| `AgentCommandBar` | `Components`, `FileAuditBundle`, `IncidentEscalation` | `ab-mud-components` |
| `AgentFileUpload` | `Components`, `FileAuditBundle` | `ab-mud-components` |
| `AgentMarkdownContent` | `MarkdownShowcase`, docs | `ab-mud-components` |
| `AgentProDashboard` | `ProDashboard` (`/demo/dashboard`) | `ab-mud-components` |

## Routes & scenarios

### Page routes
- **Status**: implemented (30 route declarations)
- **Audit evidence**: `routes[]`, including `/demo`, `/demo/components`, per-workflow
  routes, `/demo/dashboard`, `/demo/markdown-showcase`, `/demo/customization`, docs pages.
- **Note**: route prefixes in `Program.cs` (e.g. `/demo/components`, `/demo/workflows/*`)
  narrow each agent's RGPD surface.

### Launchpad scenarios
- **Status**: implemented (9 catalog entries; 3 launchpad-scale + 6 advanced)
- **Audit evidence**: `scenarios[]` — `support-quick`, `support-full`,
  `response-orchestration`, `release-dossier`, `supplier-compliance`,
  `file-audit-bundle`, `recipe-release`, `incident-escalation`, `runtime-probe`.
- **ab\* skill**: `ab-agent-registration` (routing), `ab-capability-authoring`.

## Inspector / observability

### Request-logging middleware
- **Status**: implemented
- **Where**: `Program.cs` `options.UseMiddleware<DemoChatRequestLoggingMiddleware>`.
- **Audit evidence**: `middleware[]`.
- **ab\* skill**: `ab-middleware-authoring`.

### Traffic-logging middleware
- **Status**: implemented
- **Where**: `Program.cs` `app.UseMiddleware<DemoTrafficLoggingMiddleware>`.
- **Audit evidence**: `middleware[]`.
- **ab\* skill**: `ab-middleware-authoring`.

### Log inspection endpoints
- **Status**: implemented
- **Audit evidence**: `logEndpoints[]` — `/demo-log/{login,logout,view,/,traffic,summary,download,traffic/download}`.
- **ab\* skill**: `ab-middleware-authoring`.

### Prompt tracing
- **Status**: implemented
- **Where**: `Program.cs` `agentBuilder.EnablePromptTracing()`.
- **Audit evidence**: `providers[].promptTracing = true`.
- **ab\* skill**: `ab-context-assembly`, `ab-inspector`.

### Dev tools / Agent Inspector
- **Status**: implemented (Development only) — `UseDevTools()` active when
  `builder.Environment.IsDevelopment()`.
- **Audit evidence**: `providers[].devTools = true`.
- **ab\* skill**: `ab-inspector`.

## Provider & runtime

### AI providers
- **Status**: implemented (dual)
- **Where**: `UseOpenAI(apiKey, model)` (fallback `gpt-4o-mini`) with `UseOllama(...)`
  fallback; dev/prod guard in `DemoSecurity`.
- **Audit evidence**: `providers[].useOpenAi = true`, `useOllama = true`.
- **ab\* skill**: `ab-provider-config`.

### Pro license
- **Status**: wired — `UseProLicense(licenseKey, dataDirectory)` gated by
  `AgentBlazor:LicenseKey` / `AGENTBLAZOR_LICENSE_KEY` (unset → inactive).
- **Audit evidence**: `providers[].proLicense = true`; dependent
  `SqliteActionHistoryStore`/`SqliteAgentInspectorStore` become active when key set.
- **ab\* skill**: `ab-conversation-store`, `ab-inspector`.

### Runtime probes
- **Status**: implemented
- **Where**: `RuntimeProbeWorkflow` + `RuntimeProbeCapabilities`.
- **Audit evidence**: actions `run_approval_probe`, `run_cancellation_probe`,
  `run_reconnect_probe`, `run_structured_error_date_range_probe`.
- **ab\* skill**: `ab-in-chat-features`, `ab-testing`.

## Misc

### Demo workflow domain services
- **Status**: implemented (33 service files)
- **Where**: `Services/*`, incl. `DemoWorkflowDatabaseSeeder` (SQLite seed),
  `DojoWorkspaceService`, `DemoRemoteStorageAdapter`, `JsonlDemoChatRequestLog`,
  `JsonlDemoTrafficLog`, `DemoAgentCustomizationStore`, `DemoAgentCustomizer`,
  `CustomizationDemoCapabilities`.
- **Audit evidence**: `services[]`.
- **ab\* skill**: `ab-entity-design` (EF entities in `Data/`), `ab-conversation-store`.

### Remote storage handoff (file workflow)
- **Status**: implemented (in-memory adapter; HTTP adapter configurable in
  `DemoRemoteStorage:Adapter`).
- **Where**: `DemoRemoteStorageAdapter`, `DemoFileWorkflowCapabilities`.
- **Audit evidence**: `configGates[].remoteStorageAdapter = InMemory`.
- **ab\* skill**: `ab-capability-authoring`.

### Rate limiting & security gates
- **Status**: implemented
- **Where**: `DemoSecurityOptions`, `AddRateLimiter` on agent endpoints.
- **Audit evidence**: `configGates[].rateLimitingEnabled`, `permitLimit`,
  `requireProviderInProduction`, `allowOllamaInProduction`.
- **ab\* skill**: `ab-testing` (behavioral verification), `ab-middleware-authoring`.

### Session browser / chat-history resume
- **Status**: not-demoed — no session-browser component or `GetActiveSessionsAsync`
  usage in theimplemented — see **Chat & conversation → Session browser / history resume** (`/demo/sessions` master-detail + New-chat picker + `?session=` deep links)