# Demo Features Catalog — AgentBlazor.Demo

Maintained empirical audit of demoable features in `demo/AgentBlazor.Demo`. Status is
evidence-gated — see `catalog-schema.md` for the taxonomy. Regenerate evidence with
`.github/skills/ab-demo-feature-overseer/scripts/audit-demo-features.ps1` before editing.

Last audited: 2026-08-25 (audit.json: 8 workflow agents, 3 standalone agents,
8 capability classes, 41 agent actions, 10 approvals, 2 clarification sites,
17 component page families, 25 routes, 9 launchpad scenarios)._

## Agents

### Workflow-capability agents
- **Status**: implemented (8)
- **What it demonstrates**: semantic workflow agents routing to their showcase route.
- **Where**: `Program.cs` → `AddWorkflow<TCapability>`.
- **Audit evidence**: `workflows[]` — Supplier Compliance, Support Inbox, File
  Workflow, Recipe Release, Incident Escalation, Response Orchestration, Release
  Dossier, Runtime Probe.
- **ab\* skill**: `ab-agent-registration`, `ab-capability-authoring`.

### Standalone agents
- **Status**: implemented (3)
- **What it demonstrates**: non-workflow agents with route/component scope.
- **Where**: `Program.cs` → `AddAgent`.
- **Audit evidence**: `agents[]` — Workflow Hub, Supplier Analyst, Workflow
  Orchestrator.
- **ab\* skill**: `ab-agent-registration`.

### Shared instructions & route/component scope
- **Status**: implemented
- **Where**: `agent-instructions.txt` → `WithInstructions`; `WithRoutePrefixes`,
  `WithAllowedComponents`.
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
- **Status**: implemented (8)
- **Where**: `Services/*Capabilities*.cs` and `*WorkflowService.cs`.
- **Audit evidence**: `capabilities[]` with ids `file_audit_bundle`, `recipe_release`,
  `incident_escalation`, `release_dossier`, `response_orchestration`, `runtime_probe`,
  `supplier_compliance`, `support_inbox`.
- **ab\* skill**: `ab-capability-authoring`.

### Typed agent actions
- **Status**: implemented (41 total across 8 classes)
- **Audit evidence**: `capabilities[].actions[]` (each `[AgentAction(id, description)]`).
- **ab\* skill**: `ab-capability-authoring`.

### Approval boundaries
- **Status**: implemented (10)
- **What**: destructive/mutating actions require human approval before executing.
- **Audit evidence**: `approvals[]` — `prepare_audit_bundle`, `prepare_release_draft`,
  `prepare_escalation_brief`, `submit_escalation_handoff`, `prepare_release_dossier`,
  `prepare_response_packet`, `run_approval_probe`, `prepare_remediation_draft`,
  `draft_ticket_reply_for_ticket`, `draft_ticket_reply`.
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

### Conversation persistence
- **Status**: partial — uses default store (no `Use*ConversationStore` override → InMemory).
- **Audit evidence**: empty match for conversation-store calls in Demo source.
- **ab\* skill**: `ab-conversation-store`.
- **Note**: `agentblazor-demo.db` exists in the project dir; Pro license can opt into
  durable stores (see **Pro license** / **Persistence**).

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
- **Status**: implemented (25 route declarations)
- **Audit evidence**: `routes[]`, including `/demo`, `/demo/components`, per-workflow
  routes, `/demo/dashboard`, `/demo/markdown-showcase`, docs pages.
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
- **Status**: wired-dormant — `UseDevTools()` present but commented out in `Program.cs`.
- **Audit evidence**: `providers[].devTools = false` (code commented).
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
- **Status**: implemented (25 service files)
- **Where**: `Services/*`, incl. `DemoWorkflowDatabaseSeeder` (SQLite seed),
  `DojoWorkspaceService`, `DemoRemoteStorageAdapter`, `JsonlDemoChatRequestLog`,
  `JsonlDemoTrafficLog`.
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
  usage in the Demo.
- **Note**: supported by library (`ab-chat-session-management`); not yet surfaced.