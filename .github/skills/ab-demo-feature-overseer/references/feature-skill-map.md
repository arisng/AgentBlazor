# Feature → ab\* Skill Mapping

Maps every demoable feature area in `demo/AgentBlazor.Demo` to the ab\* skill that
owns the implementation/how-to details. When auditing or editing a feature, load the
referenced skill to reason correctly about its wiring.

## How to read this

- **Demo surface**: where the feature lives in the Demo project.
- **Primary skill**: the ab\* skill to load for implementation detail.
- **Audit tokens**: the source strings `audit-demo-features.ps1` (or a human) uses to
  confirm the feature is wired.

---

## Agents

| Feature area                       | Demo surface | Primary skill                          | Audit tokens                              |
|------------------------------------|--------------|----------------------------------------|-------------------------------------------|
| Standalone agent registration      | `Program.cs` | `ab-agent-registration`                | `AddAgent("...")`                         |
| Workflow-capability agent reg      | `Program.cs` | `ab-agent-registration`                | `AddWorkflow<TCapability>("...")`         |
| Route prefixes & allowed components| `Program.cs` | `ab-agent-registration`                | `WithRoutePrefixes`, `WithAllowedComponents` |
| Shared instructions file           | `Program.cs`, `agent-instructions.txt` | `ab-context-assembly` | `WithInstructions`                    |
| Per-agent data schemas             | `Program.cs` | `ab-agent-registration`                | `AddDataSchema`, `WithDataSchemas`        |

## Capabilities & actions

| Feature area                     | Demo surface | Primary skill                    | Audit tokens                                   |
|----------------------------------|--------------|----------------------------------|------------------------------------------------|
| Semantic capability classes      | `Services/*Capabilities*.cs` & `*WorkflowService.cs` | `ab-capability-authoring` | `[AgentCapability(...)]` |
| Typed agent actions              | same files   | `ab-capability-authoring`        | `[AgentAction("...", ActionId=...)]`           |
| Approval boundaries              | same files   | `ab-in-chat-features`, `ab-capability-authoring` | `RequiresApproval = true`      |
| Clarification requests           | `SupplierCompliance*`, `SupportInbox*` | `ab-in-chat-features` | `CapabilityResult.NeedsClarification` |
| Structured outputs / next actions| `RuntimeProbeCapabilities`, workflows | `ab-capability-authoring` | `WithOutput`, `WithNextAction` |
| Recovery playbook + reset actions| many workflows | `ab-capability-authoring`      | `apply_*_recovery_playbook`, `reset_*`         |

## Chat & conversation

| Feature area                   | Demo surface                 | Primary skill                          | Audit tokens                     |
|--------------------------------|------------------------------|----------------------------------------|----------------------------------|
| Embedded chat panel            | `DemoLayout`, `Components/Pages/Demo/*` | `ab-chat-composer`          | `AgentChatSurface`               |
| Floating chat widget           | `DemoLayout`                 | `ab-chat-composer`                    | `AgentChatWidget`                |
| Generated-UI (cards) rendering | `DemoLayout`                 | `ab-in-chat-features`                  | `EnableGeneratedUi="true"`       |
| Session key isolation          | `DemoLayout`, pages via `ComponentRegistry.SessionId` | `ab-chat-session-management` | `SessionId`              |
| Conversation persistence       | Program.cs (default store)   | `ab-conversation-store`               | (probe: no store override → InMemory) |

## Agent-controllable components (MudBlazor)

| Feature area              | Demo surface                     | Primary skill          | Audit tokens                     |
|---------------------------|----------------------------------|------------------------|----------------------------------|
| Data grid                 | `Components`, `SupplierCompliance`, `SupportInbox`, `RecipeRelease` | `ab-mud-components` | `AgentDataGrid`      |
| Form                      | `Components`, `RecipeRelease`    | `ab-mud-components`    | `AgentForm`                       |
| Dialog                    | `Components`, several workflows  | `ab-mud-components`    | `AgentDialog`                     |
| Select / Autocomplete     | `Components`                     | `ab-mud-components`    | `AgentSelect`, `AgentAutocomplete`|
| Date picker / range       | `Components`                     | `ab-mud-components`    | `AgentDatePicker`, `AgentDateRangePicker` |
| Tree view                 | `Components`, `IncidentEscalation` | `ab-mud-components` | `AgentTreeView`                  |
| Stepper                   | `Components`, `IncidentEscalation` | `ab-mud-components` | `AgentStepper`                  |
| Command bar               | `Components`, `FileAuditBundle`, `IncidentEscalation` | `ab-mud-components` | `AgentCommandBar`   |
| File upload               | `Components`, `FileAuditBundle`  | `ab-mud-components`    | `AgentFileUpload`                 |
| Tabs                      | `Components`, `IncidentEscalation` | `ab-mud-components` | `AgentTabs`                      |
| Markdown content          | `MarkdownShowcase`, docs pages   | `ab-mud-components`    | `AgentMarkdownContent`            |
| Pro dashboard (live metrics)| `ProDashboard`                | `ab-mud-components`    | `AgentProDashboard`               |

## Inspector / observability

| Feature area              | Demo surface              | Primary skill                          | Audit tokens            |
|---------------------------|---------------------------|----------------------------------------|-------------------------|
| Request logging middleware| `Program.cs` (`UseMiddleware<DemoChatRequestLoggingMiddleware>`) | `ab-middleware-authoring` | `DemoChatRequestLoggingMiddleware` |
| Traffic logging middleware| `Program.cs`, `DemoTrafficLoggingMiddleware` | `ab-middleware-authoring` | `DemoTrafficLoggingMiddleware` |
| Log inspection endpoints  | `DemoLogEndpointMapper`   | `ab-middleware-authoring`              | `/demo-log/**`          |
| Prompt tracing            | `Program.cs`              | `ab-context-assembly`, `ab-inspector`  | `EnablePromptTracing`   |
| Dev tools / inspector     | `Program.cs` (`UseDevTools` is commented out) | `ab-inspector`      | `UseDevTools` (dormant) |

## Provider & runtime

| Feature area            | Demo surface      | Primary skill              | Audit tokens                        |
|-------------------------|-------------------|----------------------------|-------------------------------------|
| OpenAI provider         | `Program.cs`      | `ab-provider-config`       | `UseOpenAI(apiKey, model)`          |
| Ollama provider         | `Program.cs`      | `ab-provider-config`       | `UseOllama(...)`                    |
| Pro license (SQLite stores) | `Program.cs`  | `ab-conversation-store`, `ab-inspector` | `UseProLicense(...)`  |
| Runtime probes (cancellation etc.) | `RuntimeProbeWorkflow`, `RuntimeProbeCapabilities` | `ab-in-chat-features`, `ab-testing` | `run_cancellation_probe` etc. |

## Non-ab\* skills that also apply

- **power-shell-instr / Pester** — for `audit-demo-features.ps1` itself (scripts folder).
- **progressive disclosure** — keep `demo-features-catalog.md` lean by splitting deep
  detail into `references/`.