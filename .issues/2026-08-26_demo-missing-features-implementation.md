---
date: 2026-08-26
type: Feature Plan
severity: High
status: Done
completed_date: 2026-08-27
owner: Demo Feature Overseer
reviewed_by: Rubber-Duck Critique (2026-08-26)
---

# Implement Missing Features in AgentBlazor.Demo

## Goal

Close the coverage gap between what AgentBlazor the library supports and what the Demo
project actually exercises. The Demo currently shows 45+ features but has **14 genuinely
not-demoed features** (⛔) and **2 partial features** (🔶). Completing these makes the
Demo a faithful representative of the library's full surface, improving marketing
credibility, test coverage, and developer onboarding.

## Coverage Snapshot (2026-08-25 audit, post-critique corrections applied)

| Area | ✅ Before | ⛔ Not-Demoed | ✅ After Plan |
|---|---|---|---|
| Agents & registration | 6 | 2 (tools, MCP) | 7 |
| Capabilities & actions | 6 | 1 (handoff-approval) | 7 |
| Chat & conversation | 4 | 4 (session-browser, suggestions, insights, slash) | 8 |
| MudBlazor components | 15 | 3 (generative-surface, action-render, tool-render) | 18 |
| Inspector & observability | 5 | 1 (inspector-panel) | 6 |
| Provider & runtime | 5 | 2 (configure-chat-options, multitenancy) | 6 |
| **Total** | **41** | **13 actionable** (excl. MCP, multitenancy) | **52** |

### Corrections from Rubber-Duck Critique (2026-08-26)

| Item | Original claim | Correction |
|---|---|---|
| `[AgentParam]` | ⛔ not-demoed | **Already ✅** — 25 usages across 7 files |
| Agent Selector (1.3) | ⛔ not-demoed | **Already demoed** — widget shows selector on non-workflow routes |
| Suggestion Chips (1.4) | Phase 1 param approach | **Wrong API** — `AgentChatSurface` has no `Suggestions` param; must use `IAdaptiveSuggestionService` |
| DevTools (1.1) | Modify `DemoLayout.razor` | **Unnecessary** — `ShowDevTools` falls back to global option; only `Program.cs` change needed |
| Missing features | Not listed | `AgentActionRender`, `AgentToolRender`, `WithToolsFromAssembly` are ⛔ but omitted from plan |

---

## Phase 1 — Zero-Risk Config Flips

**Effort:** 1–2 hours | **Risk:** Minimal | **Delivers:** 3 feature flips

Each item is a parameter addition, uncomment, or config tweak on existing files.
No new service classes or pages.

### 1.1 Enable Dev Tools / Agent Inspector

| | |
|---|---|
| **Feature** | Dev Tools / Agent Inspector panel |
| **Coverage flip** | 🟡 → ✅ |
| **Files** | `Program.cs` ONLY — uncomment `options.UseDevTools()` (line ~164) |
| **Pattern** | `ab-inspector`: `UseDevTools()` registers `InMemoryAgentInspectorStore` and sets global `EnableDevTools = true` |
| **Gate** | Behind config: `if (builder.Environment.IsDevelopment()) options.UseDevTools();` or env var `DEMO_ENABLE_DEVTOOLS` |
| **Note** | `AgentChatSurface` auto-fallback: `ResolvedShowDevTools => ShowDevTools ?? AgentBlazorOptionsAccessor.Value.EnableDevTools` — no DemoLayout change needed |

### 1.2 ConfigureChatOptions Pinning

| | |
|---|---|
| **Feature** | `ConfigureChatOptions` for ReasoningEffort pinning |
| **Coverage flip** | ⛔ → ✅ |
| **Files** | `Program.cs` (add inside `AddAgentBlazor` lambda) |
| **Pattern** | `ab-provider-config`: `options.ConfigureChatOptions(o => o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None })` |
| **Note** | Primarily a GPT-5.6 safety pin; Demo defaults to `gpt-4o-mini` so this is an API showcase, not a runtime necessity |

### 1.3 Handoff Approval Policy

| | |
|---|---|
| **Feature** | `RequireHandoffApproval` + `HandoffApprovalPolicy` |
| **Coverage flip** | ⛔ → ✅ |
| **Files** | `DemoLayout.razor` (add parameters to `AgentChatSurface` and `AgentChatWidget`) |
| **Pattern** | `ab-in-chat-features`: dict maps source agent → target tokens requiring approval |
| **Policy spec** | Require approval for workflow-to-workflow handoffs (e.g., `SupportInbox` → `IncidentEscalation`). Allow free routing for `WorkflowHub` → any. |

### Phase 1 Acceptance Criteria

- [ ] `dotnet build demo/AgentBlazor.Demo` passes with zero errors
- [ ] Audit script shows `devTools: true` (or dev-only gate), `configureChatOptions: true`, `handoffApproval: true`
- [ ] Inspector panel toggle visible in chat surface (on dev builds)
- [ ] Handoff approval card renders on cross-agent switch

> **Removed from Phase 1:**
> - ~~Agent Selector~~ — already demoed on non-workflow routes via `AgentChatWidget`
> - ~~Suggestion Chips (param approach)~~ — `AgentChatSurface` has no `Suggestions` param; moved to Phase 2 as `IAdaptiveSuggestionService`

---

## Phase 2 — Service Tool + Suggestion Service + Allowed Actions

**Effort:** 3–4 hours | **Risk:** Low | **Delivers:** 3 new features

### 2.1 Register Demo Service Tool (`AddTool`)

| | |
|---|---|
| **Feature** | Service tool via `AddTool` API |
| **Coverage flip** | ⛔ → ✅ |
| **New file** | `Services/DemoServiceTools.cs` — static helper returning `AgentServiceTool` definitions |
| **Modify** | `Program.cs` — call `options.AddTool(...)` inside `AddAgentBlazor` lambda |
| **Pattern** | `ab-tool-registration`: `options.AddTool(name, desc, params, handler)` |
| **Note** | Registration happens in `Program.cs`; `DemoServiceTools.cs` holds tool definitions as reusable statics |

**Tool ideas:**
- `lookup-glossary` — takes a `term`, returns a hardcoded glossary definition
- `current-time` — returns current UTC time

### 2.2 Custom IAdaptiveSuggestionService

| | |
|---|---|
| **Feature** | Context-aware suggestion chips |
| **Coverage flip** | ⛔ → ✅ |
| **New file** | `Services/DemoSuggestionService.cs` implementing `IAdaptiveSuggestionService` |
| **Modify** | `Program.cs` — `services.AddSingleton<IAdaptiveSuggestionService, DemoSuggestionService>()` |
| **Pattern** | `ab-in-chat-features`: override `StaticSuggestionService` (internal, empty) with route-aware suggestions |
| **Note** | `TryAddSingleton` in library means consumer `AddSingleton` wins. Returns `AgentSuggestion(Text, Confidence, Source)` list based on current session/route context. |

### 2.3 WithAllowedActions Scoping

| | |
|---|---|
| **Feature** | `WithAllowedActions` on agent registration |
| **Coverage flip** | ⛔ → ✅ |
| **Modify** | `Program.cs` — on a workflow agent (not Workflow Hub), add `agent.WithAllowedActions(...)` |
| **Pattern** | `ab-tool-registration`: `WithAllowedActions(("ComponentId", "ActionId"))` |
| **Note** | `WithAllowedActions` restricts **component actions** (AgentDataGrid.Sort, AgentForm.Submit), NOT capability actions. Apply to a workflow agent that uses specific components (e.g., Support Inbox → `AgentDataGrid.`, `AgentForm.`) |

### Phase 2 Acceptance Criteria

- [ ] `dotnet build` passes
- [ ] Audit script shows `allowedActions` on Workflow Hub Agent
- [ ] `lookup-glossary` tool appears in slash command menu
- [ ] Workflow Hub Agent only exposes allowed actions

---

## Phase 3 — New Pages + Services

**Effort:** 5–7 hours | **Risk:** Medium | **Delivers:** 5 new features

> **Dependency note:** Phase 3.1 (session browser) requires Phase 3.4 (JsonFile store) to
> have durable sessions. Phase 3.3 (inspector panel) requires Phase 1.1 (UseDevTools)
> to have a populated inspector store. Sequence accordingly.

### 3.1 Session Browser Page

| | |
|---|---|
| **Feature** | Session browser / history resume |
| **Coverage flip** | ⛔ → ✅ |
| **New files** | `Pages/Demo/SessionBrowser.razor`, `Services/DemoSessionBrowserService.cs` |
| **Modify** | `DemoLayout.razor` (nav link), `DemoScenarioCatalog.cs` (scenario entry) |
| **Pattern** | `ab-chat-session-management`: `GetActiveSessionsAsync()`, `GetHistoryAsync()` |
| **Depends on** | Phase 3.4 (JsonFile store) — InMemory store loses sessions on restart |

### 3.2 AgentGenerativeSurface Showcase

| | |
|---|---|
| **Feature** | Generative UI surface with cards, tables, charts |
| **Coverage flip** | ⛔ → ✅ |
| **New file** | `Pages/Demo/GenerativeUIShowcase.razor` at `/demo/generative-ui` |
| **Modify** | `DemoLayout.razor` (nav link), `DemoScenarioCatalog.cs` |
| **Pattern** | `ab-mud-components`: `AgentGenerativeSurface` with `AgentUiDocument` blocks |
| **Note** | `EnableGeneratedUi="true"` is already set on surfaces; this page explicitly demonstrates the rendering pipeline |

### 3.3 Embedded AgentInspectorPanel

| | |
|---|---|
| **Feature** | Direct `AgentInspectorPanel` embedding |
| **Coverage flip** | ⛔ → ✅ |
| **New file** | `Pages/Demo/InspectorDebug.razor` at `/demo/debug` |
| **Modify** | `DemoLayout.razor` (nav link, conditionally when dev tools enabled) |
| **Pattern** | `ab-inspector`: `AgentInspectorPanel Inline="true" SessionId="..."` |
| **Depends on** | Phase 1.1 (UseDevTools) — inspector store must be registered |

### 3.4 Upgrade Conversation Persistence

| | |
|---|---|
| **Feature** | Durable conversation store |
| **Coverage flip** | 🔶 → ✅ |
| **Modify** | `Program.cs` — add `options.UseJsonFileConversationStore(Path.Combine(dataDir, "conversations.json"))` |
| **Pattern** | `ab-conversation-store`: `UseJsonFileConversationStore(filePath)` for <1K sessions |
| **Note** | For non-Pro scenarios only. When `UseProLicense` is active, conversations are already durable via SQLite. Gate: `if (string.IsNullOrWhiteSpace(proLicenseKey)) options.UseJsonFileConversationStore(...)` |

### 3.5 AgentActionRender / AgentToolRender Showcase

| | |
|---|---|
| **Feature** | Custom action/tool rendering components |
| **Coverage flip** | ⛔ → ✅ |
| **New file** | `Pages/Demo/ActionRenderShowcase.razor` at `/demo/action-render` |
| **Modify** | `DemoLayout.razor` (nav link), `DemoScenarioCatalog.cs` |
| **Pattern** | `ab-mud-components`: `AgentActionRender` and `AgentToolRender` wrap agent actions in custom UI |
| **Note** | These are unique selling points — custom rendering of agent actions/tools differentiates AgentBlazor from simple chat wrappers |

### Phase 3 Acceptance Criteria

- [ ] `dotnet build` passes
- [ ] `/demo/sessions` page lists past sessions with resume button (requires JsonFile store from 3.4)
- [ ] `/demo/generative-ui` page renders cards/tables/charts
- [ ] `/demo/debug` page shows full 5-tab inspector panel (requires UseDevTools from 1.1)
- [ ] `/demo/action-render` page demonstrates `AgentActionRender`/`AgentToolRender`
- [ ] Suggestion chips are route-aware (via `DemoSuggestionService` from Phase 2.2)
- [ ] Conversation history survives app restart (JsonFile store from 3.4)
- [ ] Audit script shows new routes and evidence

---

## Phase 4 — Advanced / Deferred

**Effort:** 8+ hours | **Risk:** High | **Scope:** Optional follow-up

| Feature | Why deferred |
|---|---|
| Custom `IProactiveInsightService` | LLM calls are expensive; heuristic-only demo adds limited value |
| MCP server tools (`UseMcpServer`) | Requires external MCP server endpoint; low demo value without one |
| Multitenancy (Finbuckle) | Fundamentally different deployment topology; better as standalone sample |
| UI-library coexistence | Requires adding a second component library; better as standalone sample |
| Custom `IAgentRuntimeAdapter` | Full prompt pipeline replacement; overkill for Demo |

---

## Relevant Files

### Files to modify
| File | Phases |
|---|---|
| `demo/AgentBlazor.Demo/Program.cs` | 1, 2, 3 |
| `demo/AgentBlazor.Demo/Components/Layout/DemoLayout.razor` | 1, 3 |
| `demo/AgentBlazor.Demo/Services/DemoScenarioCatalog.cs` | 3 |

### New files to create
| File | Phase |
|---|---|
| `demo/AgentBlazor.Demo/Services/DemoServiceTools.cs` | 2 |
| `demo/AgentBlazor.Demo/Services/DemoSuggestionService.cs` | 2 |
| `demo/AgentBlazor.Demo/Services/DemoSessionBrowserService.cs` | 3 |
| `demo/AgentBlazor.Demo/Components/Pages/Demo/SessionBrowser.razor` | 3 |
| `demo/AgentBlazor.Demo/Components/Pages/Demo/GenerativeUIShowcase.razor` | 3 |
| `demo/AgentBlazor.Demo/Components/Pages/Demo/InspectorDebug.razor` | 3 |
| `demo/AgentBlazor.Demo/Components/Pages/Demo/ActionRenderShowcase.razor` | 3 |

### Files needing `@using` updates
| File | Reason |
|---|---|
| `demo/AgentBlazor.Demo/Components/_Imports.razor` | New `.razor` pages may need additional `@using` directives for AgentBlazor.Components namespace |

### Reference skill files
| Skill | Why |
|---|---|
| `ab-inspector` | UseDevTools, AgentInspectorPanel wiring |
| `ab-tool-registration` | AddTool, WithAllowedActions patterns |
| `ab-in-chat-features` | Suggestions, handoff, agent selector |
| `ab-provider-config` | ConfigureChatOptions pinning |
| `ab-conversation-store` | JsonFileConversationStore |
| `ab-chat-session-management` | Session browser patterns |
| `ab-mud-components` | AgentGenerativeSurface, AgentActionRender, AgentToolRender |

---

## Decisions

1. **Skip MCP, multitenancy, UI coexistence** — architectural features better served by standalone samples
2. **Gate DevTools behind config** — don't enable unconditionally in production; dev-only by default
3. **Use JsonFileConversationStore for non-Pro only** — when Pro license is active, SQLite already provides durability; avoid split persistence model confusion
4. **Custom suggestion service uses heuristics, not LLM** — avoids API cost; demonstrates the interface contract
5. **`[AgentParam]` is already ✅** — 25 usages; correct the matrix, no code change needed
6. **Agent Selector is already ✅** — widget shows selector on non-workflow routes; remove from plan
7. **Suggestion chips require service approach** — `AgentChatSurface` has no `Suggestions` param; use `IAdaptiveSuggestionService`
8. **Add `AgentActionRender`/`AgentToolRender` showcase** — unique selling points omitted from original plan
9. **Sequence Phase 3.4 before 3.1** — session browser depends on durable store
10. **Apply `WithAllowedActions` to workflow agent, not Workflow Hub** — Hub has no `WithAllowedComponents`; workflow agents have concrete component surfaces

---

## Verification

1. `dotnet build demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj` — zero errors
2. `pwsh .github/skills/ab-demo-feature-overseer/scripts/audit-demo-features.ps1 -OutputPath artifacts/demo-audit.json`
3. Update `demoable-coverage-matrix.md`:
   - `[AgentParam]` ⛔ → ✅ (already used, stale marking)
   - Agent Selector ⛔ → ✅ (already demoed on widget)
   - Add `AgentActionRender`, `AgentToolRender` rows
4. Update `demo-features-catalog.md` with new entries
5. `dotnet test AgentBlazor.slnx` — no regressions
6. Manual smoke test: launch Demo, navigate each new page, verify end-to-end
7. Verify `_Imports.razor` has required `@using` directives for new pages
