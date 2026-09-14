---
name: ab-remote-chat
description: "Host AgentBlazor remote chat in Blazor WebAssembly — server MapAgentBlazorRemoteChat plus browser-safe AgentBlazor.Client components. Use when wiring /agentblazor/chat/run on the server, mounting AgentRemoteChatSurface/Widget/Panel/Bar in a WASM client, registering WASM HttpClient, choosing SessionId/AgentName/UserId/Context, or diagnosing remote-chat failures. Consumer-side only; never edit package internals. Triggers: MapAgentBlazorRemoteChat, AgentBlazor.Client, AgentRemoteChatSurface, AgentRemoteChatWidget, RemoteChatRunRequest, hosted WebAssembly, browser-safe chat."
metadata:
  version: 0.1.0
---

# `ab-remote-chat` — Blazor WASM Remote Chat

Consumer-side guidance for the HTTP remote-chat seam: server `POST /agentblazor/chat/run` plus browser-safe `AgentBlazor.Client` components. Written for **consumer apps referencing the public NuGet packages**, not for the AgentBlazor source repo.

## Scope — what this skill touches

- **Only consumer app code**: server `Program.cs` (runtime + endpoint) and WASM client `Program.cs`, `_Imports.razor`, pages.
- **Never edit package internals** (`_content/AgentBlazor/...`, `AgentRemoteChatSurface.razor` source, `AgentBlazorRemoteChatEndpoint` source).
- Treat both packages as a black box with the contract in [Package surface](#package-surface).

## Use this skill — not its neighbors — when

| Scenario | Use |
|---|---|
| Server `MapAgentBlazorRemoteChat()`, WASM `HttpClient`, `AgentRemoteChat*` params, remote `SessionId`, remote failures | This skill |
| InteractiveServer composer script (`AgentBlazor.min.js`), Enter-to-send toggle, prompt not cleared | `ab-chat-composer` |
| Session browser UI, `IConversationStore` impl, hydration pipeline, `::agent::` scoping | `ab-chat-session-management` + `ab-conversation-store` |
| Approvals/clarifications rendered in server-first `AgentChatSurface` | `ab-in-chat-features` |
| Existing-app scaffold leaving WASM client as manual review | `ab-cli` |

Remote chat needs **no** `AgentBlazor.min.js` / `AgentBlazorAssetPaths` / Mud providers — that entire `ab-chat-composer` chain does not apply here.

## Rule 1 — Server owns the runtime and the endpoint

Register the runtime in the **server** project, map the endpoint **before** the WASM fallback:

```csharp
// Server Program.cs
builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(apiKey: builder.Configuration["OpenAI:ApiKey"]!, model: "gpt-4o-mini");
    options.ConfigureBuilder(b => b.AddAgent("assistant", a =>
    {
        a.WithDescription("...");
        a.WithRoutePrefixes("/");
    }));
});

app.MapAgentBlazorRemoteChat(); // default pattern "/agentblazor/chat/run"
// Map before MapFallbackToFile("index.html") in hosted-WASM apps.
```

- `MapAgentBlazorRemoteChat(pattern)` maps `POST pattern` to `IAgentRuntimeAdapter.RunTurnAsync`. Empty `UserMessage` returns 400.
- Keep `MapAgentBlazorEndpoints()` (`/agentblazor/agui/run` streaming) only if a server-rendered surface also needs it; remote chat does not call it.
- Workflows/capabilities stay server-side; never reference `AgentBlazor` server package from the browser client.

## Rule 2 — Client stays browser-safe

`AgentBlazor.Client` targets `net10.0` + `browser`, depends only on `Microsoft.AspNetCore.Components.Web`. No `IJSRuntime`, no MudBlazor, no `AgentBlazor.min.js` / `AgentBlazorAssetPaths`.

The four components each ship a scoped `.razor.css` (widget/surface/panel/bar). Those are auto-included: because the client project references the RCL, the app's own stylesheet gets `@import '_content/AgentBlazor.Client/AgentBlazor.Client.bundle.scp.css'` injected at build time — so you do **not** add a manual `<link>` or `AgentBlazor.min.css`. (If a page still looks unstyled, confirm the host's `App.razor`/`index.html` actually links the app's own `{App}.styles.css`.)

```bash
dotnet add package AgentBlazor.Client # client project only
```

```csharp
// Client Program.cs — HttpClient is mandatory
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
```

```razor
@* Client _Imports.razor *@
@using AgentBlazor.Client.Chat
```

```razor
@* Any client page *@
<AgentRemoteChatSurface Endpoint="/agentblazor/chat/run"
                        Title="Assistant"
                        SessionId="hosted-wasm-surface" />
```

Variants wrap the same surface: `AgentRemoteChatWidget` (bubble + `InitiallyOpen`, `BubbleLabel`), `AgentRemoteChatPanel`, `AgentRemoteChatBar` (`EmptyText` override).

## Rule 3 — SessionId is client-owned

- Each component defaults to a fresh GUID **Guid.NewGuid()** per instance — `remote-chat-{guid}`, `remote-widget-{guid}`, `remote-panel-{guid}`, `remote-bar-{guid}`. A new instance means a new conversation.
- Pass an explicit stable `SessionId` to continue a conversation across remounts or to correlate with server history.
- Never mount server-first `AgentChatSurface/Widget/Panel/Bar` in a browser-only client project; they require circuit-scoped DI that does not exist in WASM.
- Full resolution chain (`SessionId` param → `Context["session_id"]` → `"global"` fallback at the API layer): → see `ab-chat-session-management/session-id-resolution.md` (Pattern 3 + *Fallback chain*).

## Rule 4 — Expect the shaped response

The **request** body is `{ UserMessage, AgentName, SessionId, UserId, Context }`; the **response** body is `{ AgentName, ResponseText, RequiresClarification, ClarificationQuestion, RequiresApproval, PendingApprovalCount }`. The component renders:

- Clarification → `ClarificationQuestion` text.
- Approval → `Approval required for N action(s). Continue from a server-rendered AgentBlazor surface to approve.` Approvals cannot complete from WASM.
- Otherwise → `ResponseText`.

Optional request params: `AgentName` targets one server agent, `UserId` feeds personalization/audit, `Context` (`IReadOnlyDictionary<string,string>`) flows into `AgentTurnRequest.Context`. An empty `Context` is dropped by the server.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `Remote chat is not ready yet. Ensure HttpClient is registered` | Missing WASM `HttpClient` registration — apply Rule 2 snippet. |
| `Remote chat failed with HTTP 404` | Wrong `Endpoint` or `MapAgentBlazorRemoteChat()` missing / after fallback. Verify server route and client `Endpoint` match. |
| `Remote chat failed with HTTP 400` | Empty `UserMessage`; the component already blocks empty sends, so this means a custom caller sent whitespace. |
| `Remote chat returned an empty response` | Deserialization yielded null — version mismatch between `AgentBlazor` server and `AgentBlazor.Client`; pin both to the same version. |
| History/approvals/inspector missing | By design — remote chat is stateless per turn on the client; browse/resume via server `IConversationStore` (→ see `ab-chat-session-management`). |

## Package surface (read-only)

| Surface | What it is |
|---|---|
| `MapAgentBlazorRemoteChat(pattern = "/agentblazor/chat/run")` | server endpoint extension (`AgentBlazor.Hosting` via `AgentBlazor`) |
| `AgentRemoteChatSurface/Widget/Panel/Bar` | public client components (`AgentBlazor.Client.Chat`), params `Endpoint, AgentName, SessionId, UserId, Context, Title, Eyebrow, Placeholder, EmptyText, CssClass, Style` |
| `data-testid="agent-remote-chat-surface, agent-remote-chat-input, agent-remote-chat-send, agent-remote-chat-timeline, agent-remote-chat-widget-window"` | stable selectors for Playwright (see `tests/e2e/scripts/hosted-wasm-remote-chat-runner.cjs`) |

## Related skills

- Session identity, hydration, browser UIs → see `ab-chat-session-management`.
- Store choice and `IConversationStore` impl → see `ab-conversation-store`.
- Server-first composer assets and Enter behavior → see `ab-chat-composer`.
