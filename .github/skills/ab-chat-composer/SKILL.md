---
name: ab-chat-composer
description: "Wire and diagnose the AgentChatSurface / AgentChatWidget / AgentChatPanel chat composer from a consumer app referencing the public AgentBlazor NuGet package. Use when the host app must reference AgentBlazor static assets (AgentBlazor.min.js via AgentBlazorAssetPaths.Js, .min.css via AgentBlazorAssetPaths.Css), when typed text is retained after Send (composer text retention / prompt not cleared), when diagnosing a missing script tag (AgentBlazor is not defined, setInputValue/getInputValue failing silently), or when toggling Enter-to-send vs Enter-for-newline without modifying the library. Consumer-side only: changes live in the app's host page and wwwroot; never edit package internals. Triggers: AgentBlazor.min.js, AgentBlazorAssetPaths, script loading, static assets, composer text retention, prompt not cleared, Enter to send, Shift+Enter new line, enter key behavior, attachEnterSubmit, setInputValue, getInputValue, ab-chat-surface__input."
metadata:
  version: 0.1.1
---

# `ab-chat-composer` — Composer Script Loading & Keyboard Behavior

Consumer-side guidance for the chat composer used by `AgentChatSurface`, `AgentChatWidget`, and `AgentChatPanel` — written for **consumer apps that reference the public AgentBlazor NuGet package**, not for the AgentBlazor source repo.

## Scope — what this skill touches

- **All changes happen only in the consumer app**: its own host page (`App.razor`, `_Host.cshtml`, or `index.html`) and its own `wwwroot` scripts.
- **Never edit anything under `_content/AgentBlazor/...`** — those are read-only static assets served from the installed package.
- The AgentBlazor library is treated as a **black box with a documented contract** (see [Package surface](#package-surface)). No library changes are required or attempted; no AgentBlazor source files are read or modified.
- Workarounds rely only on stable public surface: the JS API (`AgentBlazor.chat.*`), public asset constants (`AgentBlazorAssetPaths`), and the DOM classes the components render.
- You do **not** need the AgentBlazor source repo for anything in this skill.

## What do you need?

| Scenario | Go to |
|---|---|
| Load `AgentBlazor.min.js` / `.min.css` correctly, diagnose missing-script failures, verify the fix | [`references/script-loading.md`](references/script-loading.md) |
| Toggle "Enter = send" vs "Enter = new line", neutralize or remap Enter without library changes | [`references/enter-send-behavior.md`](references/enter-send-behavior.md) |

## Rule 1 — The JS asset must be loaded by the host app

The chat components send and clear composer text through the `AgentBlazor.chat` JS API shipped in the package's static web assets. Nothing works if the script is absent, and **every interop failure is silently swallowed**. If the script is missing you get the classic bug: the message sends, but the typed text stays in the box forever.

```razor
@using AgentBlazor.Components
<!-- head: -->
<link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
<!-- end of body: -->
<script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
```

- Asset constants: `AgentBlazorAssetPaths.Js` = `_content/AgentBlazor/AgentBlazor.min.js`, `AgentBlazorAssetPaths.Css` = `_content/AgentBlazor/AgentBlazor.min.css`. Both are public constants shipped in the package (`namespace AgentBlazor.Components`).
- Add the `<script>` at the **end of `<body>`** in your app's `App.razor` (Blazor Web App) or `_Host.cshtml` (legacy Blazor Server) — this is your consumer host markup, not package code.
- Quick self-check: in browser devtools console run `typeof window.AgentBlazor?.chat?.setInputValue` — must return `"function"`.

## Rule 2 — Enter behavior is hardcoded in the shipped JS; there is no library toggle

`Enter = send`, `Shift+Enter = new line` is implemented by a native JS listener (`AgentBlazor.chat.attachEnterSubmit`) that the package attaches on the surface's first interactive render. There is no `EnterToSend`-style parameter today. Flip the behavior with a small consumer-side **capture-phase** listener in your own `wwwroot` script that runs before the native handler and calls `e.stopPropagation()` — never `preventDefault()`, so the browser's default newline insertion still happens.

Complete recipes — plain newline mode, runtime toggle, Ctrl/Cmd+Enter send, handler detach, plus IME, NumpadEnter, and multi-instance caveats — are in [`references/enter-send-behavior.md`](references/enter-send-behavior.md).

## Package surface (what the installed package gives you)

| Surface | What it is | Used for |
|---|---|---|
| `_content/AgentBlazor/AgentBlazor.min.js` | static web asset served by the package — the `window.AgentBlazor.chat` JS API | script reference, runtime diagnosis |
| `_content/AgentBlazor/AgentBlazor.min.css` | static web asset — component styles | script reference |
| `AgentBlazorAssetPaths.Css` / `.Js` | public constants (`AgentBlazor.Components` namespace) | host-page asset URLs |
| `AgentChatSurface` / `AgentChatWidget` / `AgentChatPanel` | public components + their parameters (`Placeholder`, `Title`, ...) | component usage; `Placeholder` should be updated when Enter behavior changes |
| `textarea.ab-chat-surface__input`, `.ab-chat-surface__submit`, `form.ab-chat-surface__controls` | observable DOM classes rendered by the components | consumer-side JS targeting |
| `AgentBlazor.chat.*` (`attachEnterSubmit`, `detachEnterSubmit`, `setInputValue`, `getInputValue`) | runtime JS API exposed to the browser | understanding/enabling the workarounds |

> Internal method names mentioned in the references (e.g. the composer's clear path) describe **shipped-package behavior** only — they are not accessible to or editable by consumers. Any mention of `.razor`/`.cs` internals is explanatory; the AgentBlazor source repo is never required.

## Rule 3 — Consumer-side "New chat" (reset conversation) for AgentChatWidget

There is **no public new-chat/reset API** on `AgentChatWidget` in AgentBlazor 0.2.22
(`SessionId` param passthrough, auto-hydrates history). The verified consumer-side pattern
(Pass-4 #534 — `PmAgentWidget` in Playground.Lifeline) is an **outer shell with a
`@key` remount + keep-open state**:

- **Shell owns the widget:** render `AgentChatWidget` inside your own component
  (`PmAgentWidget.razor`) and pass a **fresh `@key`** (e.g. `@key=_conversationKey`) that
  changes each "New chat" click. Changing the key forces Blazor to dispose and rebuild the
  widget subtree, which resets `SessionId`/timeline without a page refresh.
- **Keep the window open:** rely on `IAgentChatWidgetState` (registered as a **singleton**
  in 0.2.22) to hold open-state. Your shell subscribes to its `Changed` event and
  re-applies `Open()` after remount via a one-shot `_reopenPending` flag. **Always
  unsubscribe in `Dispose`** — leaving a component subscribed to a singleton event keeps
  the component alive forever (memory leak).
- **New conversation identity:** generate a fresh `ConversationId` (GUID) on each New
  chat and pass it as `SessionId`; the AgentChat store returns empty history for the fresh
  id, giving an empty timeline (proven by AC-17.3/17.5 in the pass-4 browser QA).
- **Mobile caveat (AgentBlazor 0.2.22):** the widget **full-screens at mobile widths**
  while the desktop shell geometry may assume a floating 80vh window. If your shell places
  an affordance (e.g. a "New chat" chip) above the window using a formula based on
  `--pm-widget-height` (80vh), re-verify the overlap at 390×844 — the surface header can
  cover the chip and intercept pointer events (D-17.1, tracked #537). Re-derive the
  chip's `bottom` from the actual rendered window rect or relocate it for mobile.
