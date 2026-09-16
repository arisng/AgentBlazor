# Skill Catalog — Consumer Selection Detail

Full per-skill selection detail for the 20 consumer skills in `skills/`. Read
this when the compact table in `SKILL.md` is ambiguous, or when a goal touches
multiple areas and you need each skill's boundaries.

## Contents

1. [ab-agent-builder](#ab-agent-builder)
2. [ab-agent-registration](#ab-agent-registration)
3. [ab-capability-authoring](#ab-capability-authoring)
4. [ab-chat-composer](#ab-chat-composer)
5. [ab-chat-session-browser](#ab-chat-session-browser)
6. [ab-chat-session-management](#ab-chat-session-management)
7. [ab-cli](#ab-cli)
8. [ab-context-assembly](#ab-context-assembly)
9. [ab-conversation-store](#ab-conversation-store)
10. [ab-entity-design](#ab-entity-design)
11. [ab-in-chat-features](#ab-in-chat-features)
12. [ab-inspector](#ab-inspector)
13. [ab-middleware-authoring](#ab-middleware-authoring)
14. [ab-mud-components](#ab-mud-components)
15. [ab-multitenancy](#ab-multitenancy)
16. [ab-prompt-engineering](#ab-prompt-engineering)
17. [ab-provider-config](#ab-provider-config)
18. [ab-remote-chat](#ab-remote-chat)
19. [ab-tool-authoring](#ab-tool-authoring)
20. [ab-ui-integration](#ab-ui-integration)

---

## ab-agent-builder

- **Scope**: Build an "agent builder" feature — users author custom agents
  on-demand, persisted to SQL Server. Covers the `AgentDefinitionEntity`
  subclass + DbContext (TPC, unique CI name index), the store-backed
  `IAgentRegistry` replacement (`SqlServerAgentRegistry` with lazy load,
  `AddOrUpdate`/`RemoveAgent`/`RefreshFromDatabase`/`TryGetCustomization`),
  idempotent seeding, and the store-backed `IAgentRegistry` as the authoring
  surface (the UI calls `GetAll`/`AddOrUpdate`/`RemoveAgent` directly) with
  `UpdatedAtUtc` concurrency and capability/tool discovery seams.
- **Signals**: agent builder, author custom agents, runtime agent authoring,
  user-defined agents, store-backed registry, SQL Server agent definitions,
  `AgentDefinitionEntity`, `SqlServerAgentRegistry`, `IAgentRegistry` replace.
- **Boundaries**: does NOT cover static registration wiring
  (`ab-agent-registration`), entity/migration design (`ab-entity-design`), or
  prompt/context assembly (`ab-context-assembly`) — it orchestrates them.
  UI-library-agnostic: no component guidance (see `ab-mud-components` /
  `ab-ui-integration` for surfaces). Single-tenant only; tenancy is
  `ab-multitenancy`'s job.
- **Related**: `ab-agent-registration`, `ab-entity-design`,
  `ab-context-assembly`, `ab-capability-authoring`, `ab-tool-authoring`.

## ab-agent-registration

- **Scope**: Register agents and workflow-capability agents — statically at
  startup (`AddAgent`, `AddWorkflow`, `AgentRegistrationBuilder`,
  `WithRoutePrefixes`, `WithAllowedComponents`, `WithAllowedActions`,
  `WithAllowedCapabilityActions`, `WithDataSchemas`, `WithInstructions`,
  `WithToolsFromAssembly`, `ConfigureBuilder`, `AgentBlazorBuilder`) or
  dynamically at runtime via a custom `IAgentRegistry` (`InMemoryAgentRegistry`,
  per-tenant / store-backed / runtime agent sets, `AddOrUpdate`).
- **Signals**: agent names/descriptions/instructions, route bindings, component
  access, tool access, data schemas, dynamic agents, per-tenant agents,
  store-backed agents, replacing `IAgentRegistry`.
- **Boundaries**: does NOT cover authoring capability classes
  (`ab-capability-authoring`), tool definitions (`ab-tool-authoring`), or
  prompt prose (`ab-prompt-engineering`) — it wires them onto agents.
- **Related**: `ab-capability-authoring`, `ab-tool-authoring`,
  `ab-context-assembly`, `ab-prompt-engineering`.

## ab-capability-authoring

- **Scope**: Author semantic capability classes with `[AgentCapability]` /
  `[AgentAction]` / `[AgentParam]` methods, returning `CapabilityResult`,
  setting approval boundaries (`RequiresApproval`), shaping structured outputs
  (`WithOutput`), warnings, next actions, clarifications.
- **Signals**: `AgentCapabilityAttribute`, `AgentActionAttribute`,
  `AgentParamAttribute`, `CapabilityResult`, `RequiresApproval`,
  `WithNextActions`, `WithOutput`, `WithWarnings`, `AddCapability`,
  `AgentCapabilityDescriptor`, `ClarificationQuestion`,
  `AgentRuntimeContextKeys`, `ContextKey`, `AvailableWhen`.
- **Boundaries**: does NOT cover registering the capability onto an agent
  (`ab-agent-registration`), the approval UX in chat
  (`ab-in-chat-features`), or prompt alignment (`ab-prompt-engineering`).
- **Related**: `ab-agent-registration`, `ab-in-chat-features`,
  `ab-prompt-engineering`, `ab-tool-authoring`.

## ab-chat-composer

- **Scope**: Wire and diagnose the chat composer of `AgentChatSurface` /
  `AgentChatWidget` / `AgentChatPanel` from a consumer app — static asset
  references (`AgentBlazor.min.js` via `AgentBlazorAssetPaths.Js`, `.min.css`),
  composer text retention after Send, missing script tags, Enter-to-send vs
  Enter-for-newline.
- **Signals**: `AgentBlazor.min.js`, `AgentBlazorAssetPaths`, script loading,
  static assets, prompt not cleared, Enter to send, Shift+Enter new line,
  `attachEnterSubmit`, `setInputValue`, `getInputValue`,
  `ab-chat-surface__input`.
- **Boundaries**: consumer-side only (host page + wwwroot); never edit package
  internals. Does NOT cover session browsing (`ab-chat-session-browser` /
  `ab-chat-session-management`) or in-chat interactive features
  (`ab-in-chat-features`).
- **Related**: `ab-chat-session-browser`, `ab-chat-session-management`,
  `ab-in-chat-features`, `ab-mud-components`, `ab-ui-integration`.

## ab-chat-session-browser

- **Scope**: Compose a master-detail session browser page — session list
  panel, detail panel with resume or new-chat, agent picker for new
  conversations, deep-linking via query params, and `AgentChatSurface`
  parameter constraints (`LockAgentToCurrentRoute`, `EnableAgentHandoff`,
  `RequireHandoffApproval`, `ShowAgentSelector`, `@key` strategy) for
  correct browser hydration. Consumer-side data service pattern over
  `IConversationStore` + `IAgentRegistry`, legacy session handling
  (`SplitSessionKey`), token usage display, and the new-chat draft
  lifecycle (mint → chat → promote → resume). UI-implementation agnostic.
- **Signals**: session browser, master-detail, session list, session picker,
  browse past chats, resume session, new chat page, agent picker for
  sessions, session browser layout, session deep-link.
- **Boundaries**: backend session lifecycle, ID resolution, and hydration
  internals are `ab-chat-session-management`. Agent authoring at runtime is
  `ab-agent-builder`. Component library wrappers are `ab-mud-components`.
  Does NOT cover delete/end session (no API), search/filter (v2), or store
  implementation (`ab-conversation-store`).
- **Related**: `ab-chat-session-management`, `ab-agent-builder`,
  `ab-conversation-store`, `ab-mud-components`.

## ab-chat-session-management

- **Scope**: Browse past conversations, select and resume sessions, hydrate
  chat UI from stored history — `GetActiveSessionsAsync`,
  `GetSessionsForUserAsync`, `GetHistoryAsync`, session-key isolation
  (`AgentConversationScope`), `SetUserIdAsync`, hydration pipeline
  (`HydrateTimelineFromHistoryAsync`, `TryResumeActiveRunAsync`).
- **Signals**: session browser, session list, resume chat, browse past chats,
  session selector, chat history browser, switch session, load session.
- **Boundaries**: assumes a conversation store exists; choosing/implementing
  the store is `ab-conversation-store`. UI composition for session browsing
  (master-detail layout, agent picker, new-chat draft) is
  `ab-chat-session-browser`. Does NOT cover the composer
  (`ab-chat-composer`).
- **Related**: `ab-chat-session-browser`, `ab-conversation-store`,
  `ab-chat-composer`, `ab-mud-components`.

## ab-cli

- **Scope**: Drive the `agentblazor` CLI (`AgentBlazor.Cli` NuGet package) to
  wire AgentBlazor into **existing** Blazor apps — `analyze`, `scaffold` /
  `scaffold workflows`, `doctor` / `validate`, `init` / `update` / `watch` for
  `.agentblazor/AGENT.md`, `.agentblazorc` and provider env vars.
- **Signals**: onboarding an existing .NET/.sln/.slnx solution, app analysis
  report, scaffolding wiring/workflows, install check, AGENT.md regeneration.
- **Boundaries**: NOT for greenfield app creation or authoring
  `[AgentCapability]`/`[AgentAction]` attributes directly (use the library +
  `ab-capability-authoring`).
- **Related**: `ab-agent-registration`, `ab-capability-authoring`,
  `ab-prompt-engineering` (post-scaffold follow-ups).

## ab-context-assembly

- **Scope**: How AgentBlazor assembles the full LLM context — system prompt
  construction, dynamic runtime context injection (`AgentRuntimeContextKeys`),
  turn enrichment via middleware, prompt tracing (`EnablePromptTracing`),
  replacing the runtime adapter (`UseRuntimeAdapter`, `IAgentRuntimeAdapter`).
- **Signals**: system prompt, instructions, `WithInstructions`,
  `AgentRuntimeContextKeys`, context dictionary, prompt tracing,
  `PromptTracingOptions`, dynamic context, runtime context, prompt pipeline,
  `IAgentRuntimeAdapter`, `UseRuntimeAdapter`, `IAgentTurnMiddleware`,
  `AgentTurnContext`, context injection.
- **Boundaries**: consumer-side only. Does NOT cover authoring middleware
  classes (`ab-middleware-authoring`) or aligning prompt prose with the
  registered surface (`ab-prompt-engineering`).
- **Related**: `ab-prompt-engineering`, `ab-middleware-authoring`,
  `ab-inspector`, `ab-agent-registration`.

## ab-conversation-store

- **Scope**: Conversation history storage — choosing between
  `InMemoryConversationStore`, `JsonFileConversationStore`, or a custom durable
  EF Core + SQL Server store; incremental persistence (`AppendTurnAsync`,
  `UpdateTurnAsync`, `DeleteTurnAsync`, `ReorderTurnsAsync` keyed by
  `ConversationTurn.TurnId`); `UseJsonFileConversationStore`; action history
  (`IActionHistoryStore`, `SqliteActionHistoryStore`, `UseProLicense`).
- **Signals**: `IConversationStore`, `UseConversationStore`,
  `UseJsonFileConversationStore`, `InMemoryConversationStore`,
  `JsonFileConversationStore`, `AppendTurnAsync`, `UpdateTurnAsync`,
  `DeleteTurnAsync`, `ReorderTurnsAsync`, `TurnId`, conversation persistence,
  incremental persistence, `IActionHistoryStore`, `ActionHistoryEntry`,
  `SqliteActionHistoryStore`, `UseProLicense`, agent action persistence.
- **Boundaries**: does NOT cover session-browser UIs
  (`ab-chat-session-management`).
- **Related**: `ab-chat-session-management`, `ab-multitenancy`.

## ab-entity-design

- **Scope**: EF Core domain entities — abstract base classes
  `ConversationSessionEntity`, `ConversationTurnEntity`, `AgentDefinitionEntity`;
  consumer inheritance pattern (TPC mapping); composite vs surrogate keys; FK
  cascades and indexes; `IsolateConversationsByAgent` implications; audit columns, soft delete,
  concurrency tokens; JSON columns vs owned entity types; migrations.
- **Signals**: entity design, domain entities, EF Core entities, entity
  relationships, FK cascade, composite key, global query filter, consumer
  extensions (`BaseSessionId`, `AgentName`, `TenantId`), owned entity types, split queries,
  concurrency token, audit columns, soft delete.
- **Boundaries**: entity design only; store wiring is
  `ab-conversation-store`, tenant resolution is `ab-multitenancy`.
- **Related**: `ab-conversation-store`, `ab-multitenancy`,
  `ab-agent-registration` (store-backed registries).

## ab-in-chat-features

- **Scope**: In-chat interaction features — approval dialogs
  (`RequiresApproval`), clarification (`NeedsClarification`), handoff approval
  (`RequireHandoffApproval`, `HandoffApprovalPolicy`), generated-UI cards
  (`action.confirmation`), suggestion chips, proactive insights, next actions,
  warnings, reasoning, execution details, slash commands, agent selector, stop
  button, timeout warning, error boundary, dev tools (`ShowDevTools`).
- **Signals**: approval dialog, clarification, handoff approval, generated UI,
  suggestion chips, proactive insight, slash commands, agent selector, stop
  button, timeout, error boundary, dev tools.
- **Boundaries**: consumer-side only. Does NOT cover authoring the capability
  that requests approval (`ab-capability-authoring`) or the composer
  (`ab-chat-composer`).
- **Related**: `ab-capability-authoring`, `ab-chat-composer`,
  `ab-inspector` (dev tools), `ab-mud-components`.

## ab-inspector

- **Scope**: Agent Inspector — records every agent run into an
  `IAgentInspectorStore`, five-tab panel (Runs, Events, Prompt, State,
  Components); enabling via `UseDevTools` (no license) or `UseProLicense`
  (durable `SqliteAgentInspectorStore`); `ShowDevTools` / `AutoShowDevTools`;
  correlating multi-agent handoff chains.
- **Signals**: agent inspector, inspector, dev tools, `ShowDevTools`,
  `AutoShowDevTools`, `UseDevTools`, `AgentInspectorPanel`,
  `IAgentInspectorStore`, `InspectorRunRecord`, `InspectorEvent`,
  `SqliteAgentInspectorStore`, `InMemoryAgentInspectorStore`, debug agent runs,
  event timeline, system prompt replay, handoff chain.
- **Boundaries**: consumer-side only. Debugging *why* a run misbehaved often
  continues into `ab-context-assembly` / `ab-prompt-engineering`.
- **Related**: `ab-context-assembly`, `ab-prompt-engineering`,
  `ab-in-chat-features` (dev tools toggle).

## ab-middleware-authoring

- **Scope**: Custom middleware in the agent turn pipeline —
  `IAgentTurnMiddleware`, `AgentTurnContext`, cross-cutting concerns (logging,
  cost control, tenant enrichment, audit, rate limiting), short-circuiting
  turns, inline delegates vs typed middlewares, execution order, scope
  resolution.
- **Signals**: `IAgentTurnMiddleware`, `AgentTurnContext`, `UseMiddleware`,
  `AgentMiddlewarePipeline`, `IAgentExecutionScopeAccessor`, short-circuit,
  middleware pipeline, agent turn middleware.
- **Boundaries**: NOT ASP.NET Core middleware; runs inside each agent turn.
  Does NOT cover context injection via the runtime
  (`ab-context-assembly`).
- **Related**: `ab-context-assembly`, `ab-multitenancy` (tenant enrichment),
  `ab-inspector`.

## ab-mud-components

- **Scope**: All AgentBlazor components built on MudBlazor — shell providers,
  chat surfaces, agent-controllable wrappers (`AgentDataGrid`, `AgentForm`,
  `AgentDialog`, `AgentSelect`, `AgentAutocomplete`, `AgentDatePicker`,
  `AgentDateRangePicker`, `AgentTreeView`, `AgentStepper`, `AgentTabs`,
  `AgentCommandBar`, `AgentNavMenu`, `AgentFileUpload`), generative UI blocks
  (`AgentGenerativeSurface`, `AgentGeneratedCard/Form/Chart/Table`), base
  classes (`AgentControllableComponentBase`, `AgentFormPageBase`), attributes,
  action rendering (`AgentActionRender`, `AgentToolRender`),
  `AgentProDashboard`, `AgentInspectorPanel`.
- **Signals**: building UIs with MudBlazor-backed components, wiring agent
  actions to MudBlazor elements, custom controllable components, debugging
  component-agent interaction.
- **Boundaries**: MudBlazor provider only. Does NOT cover coexisting with other
  UI libraries (`ab-ui-integration`) or the composer (`ab-chat-composer`).
- **Related**: `ab-ui-integration`, `ab-chat-composer`,
  `ab-in-chat-features`, `ab-inspector`.

## ab-multitenancy

- **Scope**: Multi-tenant production deployments on Blazor Interactive Server
  with BFF + API + SQL Server — tenant resolution (Finbuckle.MultiTenant),
  per-tenant LLM provider via proxy `IChatClient`, per-tenant EF Core
  conversation/data stores, middleware for cost control and tenant enrichment,
  full BFF integration pattern.
- **Signals**: multi-tenant, multitenant, SaaS, tenant isolation, per-tenant,
  Finbuckle, BFF pattern, tenant context, `TenantAwareChatClient`, proxy
  `IChatClient`, productionizing AgentBlazor for SaaS.
- **Boundaries**: deployment-level concern; per-tenant options pinning is
  `ab-provider-config`.
- **Related**: `ab-provider-config`, `ab-middleware-authoring`,
  `ab-conversation-store`.

## ab-prompt-engineering

- **Scope**: Craft and keep aligned agent system prompts (`WithInstructions`)
  with the registered surface — capabilities, actions, components, tools,
  approval gates, clarifications, data schemas. Audits for
  hallucinated/omitted/over-used actions and approval-boundary drift.
- **Signals**: system prompt, `WithInstructions`, align prompt, prompt
  alignment, agent prompt, refine prompt, craft prompt, hallucinated action,
  agent calls wrong action, prompt drift, prompt out of sync, prompt checklist,
  prompt survey, `WithDescription`, `WithDataSchemas`.
- **Boundaries**: consumer-side only. Runs AFTER the surface is registered —
  authoring the surface is `ab-capability-authoring` / `ab-tool-authoring` /
  `ab-agent-registration`.
- **Related**: `ab-capability-authoring`, `ab-tool-authoring`,
  `ab-agent-registration`, `ab-context-assembly`.

## ab-provider-config

- **Scope**: Provider seam — pinning `ChatOptions` that flow to the provider
  on every turn (`ConfigureChatOptions`, v0.2.23+); model rejects function
  tools (HTTP 400 `reasoning_effort`); gpt-5.6-luna/-sol/-terra needs
  `ReasoningEffort.None`; per-provider option mapping (OpenAI vs Azure vs
  Ollama vs OriginAI); per-tenant options behind a proxy `IChatClient`.
- **Signals**: `reasoning_effort`, gpt-5.6, gpt-5.6-luna, gpt-5.6-sol,
  gpt-5.6-terra, function tools rejected, 400 reasoning_effort,
  `ReasoningEffort`, `ConfigureChatOptions`, `ChatOptions`, provider options,
  Responses API, pin reasoning effort.
- **Boundaries**: consumer-side only. Does NOT cover provider *selection* at
  registration time (`ab-agent-registration`) or tenant resolution
  (`ab-multitenancy`).
- **Related**: `ab-multitenancy`, `ab-agent-registration`, `ab-inspector`.

## ab-remote-chat

- **Scope**: Remote chat in Blazor WebAssembly — server
  `MapAgentBlazorRemoteChat` (`/agentblazor/chat/run`) plus browser-safe
  `AgentBlazor.Client` components (`AgentRemoteChatSurface/Widget/Panel/Bar`),
  WASM `HttpClient` registration, `SessionId`/`AgentName`/`UserId`/`Context`
  selection, remote-chat failure diagnosis.
- **Signals**: `MapAgentBlazorRemoteChat`, `AgentBlazor.Client`,
  `AgentRemoteChatSurface`, `AgentRemoteChatWidget`, `RemoteChatRunRequest`,
  hosted WebAssembly, browser-safe chat.
- **Boundaries**: consumer-side only. Does NOT cover the server-side composer
  (`ab-chat-composer`) or persistence (`ab-conversation-store`).
- **Related**: `ab-chat-composer`, `ab-conversation-store`,
  `ab-chat-session-management`.

## ab-tool-authoring

- **Scope**: Service tools, MCP servers, and the tool surface — `AddTool`,
  `UseMcpServer`, `AgentToolParameter`, handler delegates with DI access,
  per-agent filtering (`WithAllowedActions`), tool resolution order and
  execution dispatch, tool errors and approval gates.
- **Signals**: `AddTool`, `UseMcpServer`, `AgentServiceTool`,
  `AgentToolParameter`, `IAgentServiceToolRegistry`, `IMcpToolProvider`,
  `HttpMcpToolProvider`, `WithAllowedActions`, tool resolution order, tool
  execution dispatch, `EnabledToolIds`, `IAgentRuntimeCustomizer`,
  `RequiresApproval`, tool naming, `NormalizeToolName`, `ToolCallStart`,
  `ToolCallResult`, `ToolCallEnd`.
- **Boundaries**: does NOT cover capability actions
  (`ab-capability-authoring`) or prompt alignment after adding tools
  (`ab-prompt-engineering`).
- **Related**: `ab-agent-registration`, `ab-prompt-engineering`,
  `ab-capability-authoring`.

## ab-ui-integration

- **Scope**: Integrate AgentBlazor (with its MudBlazor dependency) into a
  Blazor project that already uses another UI library — Telerik, Radzen,
  Syncfusion, DevExpress, Blazorise, Ant Design Blazor, or custom CSS — without
  class-name or style conflicts.
- **Signals**: "CSS conflict", "CSS clash", "MudBlazor conflict", "coexist
  with [library]", "add AgentBlazor to existing app", "integrate AgentBlazor
  with [library]", "prevent CSS leaking", "CSS isolation with AgentBlazor",
  "AgentBlazor and MudBlazor setup", "style collision", "component library
  conflict", "avoid MudBlazor breaking my UI".
- **Boundaries**: integration/conflict surface only; component usage is
  `ab-mud-components`.
- **Related**: `ab-mud-components`, `ab-chat-composer`.
