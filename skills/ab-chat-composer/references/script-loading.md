# Script Loading — AgentBlazor Chat Assets

Root cause and fix for the most common chat composer failure: **the message sends, but the typed text stays in the input box** (composer text retention).

## Contents

- [Script Loading — AgentBlazor Chat Assets](#script-loading--agentblazor-chat-assets)
  - [Contents](#contents)
  - [Why this happens](#why-this-happens)
  - [The fix — reference the assets in the host page](#the-fix--reference-the-assets-in-the-host-page)
    - [Blazor Web App (.NET 8+), `App.razor`](#blazor-web-app-net-8-apprazor)
    - [Legacy Blazor Server (`_Host.cshtml`)](#legacy-blazor-server-_hostcshtml)
    - [Blazor WebAssembly (`index.html`)](#blazor-webassembly-indexhtml)
  - [Diagnosis checklist](#diagnosis-checklist)
  - [Failure modes of the interop chain](#failure-modes-of-the-interop-chain)
  - [Shipped artifacts (read-only — do not modify)](#shipped-artifacts-read-only--do-not-modify)

## Why this happens

The shipped `AgentChatSurface` component renders its composer textarea deliberately **not** `@bind`-bound:

```razor
<textarea id="@PromptId"
          class="ab-chat-surface__input"
          @oninput="HandlePromptInput"   <!-- pushes DOM -> server only -->
          @onkeydown="HandlePromptKeyDown"
          ... />
```

`@oninput` only copies the DOM value into the component's server-side state. Blazor never writes a `value` back into this element on re-render, so **only JS can clear the visible text**. Clearing is a single interop call whose failures are silently ignored (shipped-package behavior):

```csharp
// package internals — described here only to explain the observed behavior
await JSRuntime.InvokeVoidAsync("AgentBlazor.chat.setInputValue", promptTextareaId, value);
// catch { } // <- every failure is swallowed, no log, no fallback
```

The JS side is also silent when the element is missing:

```js
setInputValue: function (elementId, value) {
  var input = document.getElementById(elementId);
  if (!input) return;            // element missing -> silent no-op
  input.value = value || '';
  input.dispatchEvent(new Event('input', { bubbles: true }));
}
```

If `AgentBlazor` is undefined entirely, `setInputValue` throws `ReferenceError` → swallowed → the DOM text stays forever. The send itself still works because it is a pure .NET path (`EditForm` submit), and the send path reads the DOM value via `getInputValue` first, falling back to the component's last-known server value when JS is unavailable.

## The fix — reference the assets in the host page

Asset constants (public, shipped in the package — `namespace AgentBlazor.Components`):

```csharp
public const string Css = "_content/AgentBlazor/AgentBlazor.min.css";
public const string Js  = "_content/AgentBlazor/AgentBlazor.min.js";
```

The `_content/AgentBlazor/...` root is the NuGet **package id**, not the assembly name — a URL like `_content/AgentBlazor.Components/...` 404s.

### Blazor Web App (.NET 8+), `App.razor`

```razor
@using AgentBlazor.Components

<head>
    <link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
    ...
</head>
<body>
    ...
    <script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
</body>
```

The `<script>` must appear at the **end of `<body>`** (after Blazor's own `blazor.web.js`) in your consumer app's host page. These are your own host markup files (`App.razor` / `_Host.cshtml` / `index.html`) — not package files.

### Legacy Blazor Server (`_Host.cshtml`)

```html
<link rel="stylesheet" href="_content/AgentBlazor/AgentBlazor.min.css" />
...
<script src="_content/AgentBlazor/AgentBlazor.min.js"></script>
```

### Blazor WebAssembly (`index.html`)

Same plain URLs as legacy, placed in `<head>` (CSS) and end of `<body>` (JS).

> The chat components only need `AgentBlazor.min.js`. The MudBlazor provider components (`AgentBlazorShell` and friends) additionally require `_content/MudBlazor/MudBlazor.min.js` — that is a separate concern from this skill.

## Diagnosis checklist

| Symptom | Likely cause | Check |
|---|---|---|
| Message sends, composer text retained forever | `AgentBlazor.min.js` never loaded | Console: `typeof window.AgentBlazor?.chat?.setInputValue` → `"undefined"` |
| Composer text cleared on some pages but not others | Script only referenced in one host page | Compare `<script>` tags across `App.razor` / layout / per-page hosts |
| Text retained only on first load, OK after refresh | `<script>` injected by interactively-rendered markup, or a load-order race on first navigation | `OnAfterRenderAsync` does **not** run during static SSR, so interop cannot fail "during prerender"; keep the `<script>` in static host markup (`App.razor` / `_Host.cshtml`) loaded before the circuit attaches; a refresh re-attaches |
| 404 in Network tab for `AgentBlazor.min.js` | Package not referenced or wrong path | Confirm package is installed and use the `_content/AgentBlazor/...` path (not `_content/AgentBlazor.Components/...`) |

Diagnosis steps:

1. Open browser devtools Console and run `typeof window.AgentBlazor?.chat?.setInputValue`.
2. If it is not `"function"`, the script is missing — add the `<script>` tag (see above).
3. Check the Network tab for the `AgentBlazor.min.js` request; a 404 means a wrong path or an unreferenced package.
4. After adding the tag: type a message, Send, and confirm the textarea clears.

## Failure modes of the interop chain

All of these fail silently by design — the only symptom is a retained/non-cleared DOM value:

| Failure | Where it dies (shipped-package behavior) |
|---|---|
| `AgentBlazor` undefined (script not loaded) | `ReferenceError` inside the interop call → swallowed by `catch` |
| Element id not found (timing, or id differs per instance) | `if (!input) return;` in `setInputValue`/`getInputValue` |
| `IJSRuntime` unavailable (component tests, headless/SSR host) | composer clear interop `catch` — DOM not updated, falls back to server-side state |
| Blazor Server fast-typing race (stale `oninput` after clear) | a late `oninput` re-populates the component's server-side prompt state after the clear ran — the DOM is already empty, but the Send button becomes enabled and clicking it does nothing (the send path reads the DOM via `getInputValue`, which returns empty) |

Note that `PromptId` is a **per-instance GUID** (`ab-agent-prompt-{instanceId}`), so you cannot reliably hardcode it in external scripts — target elements by class instead (see `enter-send-behavior.md`).

## Shipped artifacts (read-only — do not modify)

Everything this skill relies on comes from the installed NuGet package and is read-only from the consumer's perspective:

| Artifact | Purpose |
|---|---|
| `_content/AgentBlazor/AgentBlazor.min.js` | the JS API (`AgentBlazor.chat.setInputValue`, `getInputValue`, `attachEnterSubmit`, `detachEnterSubmit`, `scrollToBottom`) |
| `_content/AgentBlazor/AgentBlazor.min.css` | component styles |
| `AgentBlazorAssetPaths.Css` / `.Js` | public constants for host-page asset URLs |
| `AgentChatSurface` component + rendered DOM | the composer textarea (`.ab-chat-surface__input`) and submit button (`.ab-chat-surface__submit[type="submit"]`) |

> Interop methods named in this document (the composer clear/send paths) are **package internals** — they exist only to explain observed behavior. Do not attempt to locate or edit them; they are not part of the consumer-facing contract.
