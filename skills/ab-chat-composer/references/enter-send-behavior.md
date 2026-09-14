# Enter = Send vs Enter = New Line — Consumer-Side Toggles

How the hardcoded behavior works, and consumer-side workarounds that flip it **without library changes**.

## Contents

- [How the hardcoded behavior works](#how-the-hardcoded-behavior-works)
- [Composer element anatomy](#composer-element-anatomy)
- [Workaround A — neutralize Enter-send entirely](#workaround-a--neutralize-enter-send-entirely)
- [Workaround B — runtime toggle](#workaround-b--runtime-toggle)
- [Workaround C — modifier-based send](#workaround-c--modifier-based-send-ctrlenter)
- [IME composition caveat](#ime-composition-caveat)
- [Placement & caveats](#placement--caveats)

## How the hardcoded behavior works

The contract is `Enter = send`, `Shift+Enter = new line` — no toggle parameter exists on `AgentChatSurface`, `AgentChatWidget`, or `AgentChatPanel`.

1. On the surface's **first interactive render**, the shipped package attaches the Enter-to-send handler, roughly:

   ```csharp
   // package internals — shown only to explain the observed behavior
   await JSRuntime.InvokeVoidAsync("AgentBlazor.chat.attachEnterSubmit", promptTextareaId, submitButtonId);
   ```

2. The JS listener in `AgentBlazor.min.js` (shipped code, observable in the browser at `_content/AgentBlazor/AgentBlazor.min.js`):

   ```js
   attachEnterSubmit: function (textareaId, submitButtonId) {
     if (textarea._abChatEnterHandler) {        // re-attach guard: drop any stale handler first
       textarea.removeEventListener('keydown', textarea._abChatEnterHandler);
       textarea._abChatEnterHandler = null;
     }
     var handler = function (e) {
       if ((e.key === 'Enter' || e.key === 'NumpadEnter') && !e.shiftKey) {
         e.preventDefault();
         submitBtn.click();            // Enter = send
       }
     };
     textarea.addEventListener('keydown', handler);   // BUBBLE phase, on the element
     textarea._abChatEnterHandler = handler;          // stored so detachEnterSubmit can remove it
   }
   ```

3. The handler is attached only on the surface's **first interactive render** and detached when the component is disposed. The package's Blazor-side key handler only handles `Escape` (slash-menu dismissal) — Enter never reaches .NET. (All of these are shipped-package internals; consumers observe the behavior but do not edit the package.)
4. `AgentChatWidget` and `AgentChatPanel` simply host an `AgentChatSurface`, so this applies to all three. Closing the widget only hides it — the surface stays mounted, so its handler is **not** re-attached on reopen (only a parent unmount/remount recreates it).

## Composer element anatomy

| Element | Selector | Notes |
|---|---|---|
| Prompt textarea | `textarea.ab-chat-surface__input` | `id = ab-agent-prompt-{guid}` (per-instance, not stable) |
| Send button | `.ab-chat-surface__submit[type="submit"]` | the only `type="submit"` button; inside the composer `<form class="ab-chat-surface__controls">` |
| Stop button | `.ab-chat-surface__submit` (`type="button"`) | rendered only while a run can be cancelled |
| Clarification input | `input.ab-chat-surface__input` | shares the class — **always scope to `textarea`** |
| Clarification submit | `.ab-chat-surface__submit` (`type="button"`) | rendered earlier in the DOM than the composer while a clarification is pending |

> `.ab-chat-surface__submit` is shared by **three** buttons (send, Stop, clarification submit) — never target it bare. Only the send button is `type="submit"`.

## Workaround A — Neutralize Enter-send entirely (Enter always = new line)

Two techniques. Prefer the capture-phase listener (A1): it is timing-independent, covers every surface instance, and needs no knowledge of internals. A2 detaches the stored handler directly but is timing-sensitive.

### A1. Capture-phase interception (recommended)

Attach a `capture` listener on `document` that runs **before** the element's bubble-phase listener. Call `stopPropagation()` to keep the native handler from firing, but **do not** call `preventDefault()` — that preserves the browser's default newline insertion.

```html
<script>
  document.addEventListener('keydown', function (e) {
    if (e.isComposing) return; // never interfere with IME composition
    var isEnter = e.key === 'Enter' || e.key === 'NumpadEnter'; // match the native handler's key set
    if (isEnter && e.target && e.target.matches('textarea.ab-chat-surface__input')) {
      e.stopPropagation(); // AgentBlazor's send handler never fires
      // no preventDefault() -> browser inserts the newline
    }
  }, true);
</script>
```

Sending is then only possible via the Send button (or the modifier recipe in Workaround C).

### A2. Detach the stored handler directly

The library stores its handler on the element as `_abChatEnterHandler`, so you can remove it yourself (equivalent to `detachEnterSubmit`, which needs the private per-instance id). **Poll instead of `window.load`**: `attachEnterSubmit` runs on the first *interactive* render, which on a prerendered page can happen after `load` — a one-shot `load` listener silently no-ops in that case.

```html
<script>
  function detachAllEnterSend() {
    var anyDetached = false;
    document.querySelectorAll('textarea.ab-chat-surface__input').forEach(function (ta) {
      if (ta._abChatEnterHandler) {
        ta.removeEventListener('keydown', ta._abChatEnterHandler);
        ta._abChatEnterHandler = null;
        anyDetached = true;
      }
    });
    return anyDetached;
  }

  (function retry() {
    if (!detachAllEnterSend()) setTimeout(retry, 250); // no surface, or handler not attached yet
  })();

  // Blazor Web App enhanced navigation recreates surfaces, which re-attaches the
  // handler. The IIFE above runs once per full load, so re-run the detach after nav:
  document.addEventListener('blazor:enhancednavigationend', detachAllEnterSend);
</script>
```

Once detached, the handler stays off for that surface instance until the surface is recreated (full reload, or a parent unmounts/remounts it — closing the widget does **not** recreate it).

## Workaround B — Runtime toggle

A flag + capture listener + a small toggle UI. In "send mode" do nothing (the native handler sends on Enter). In "newline mode" stop propagation so Enter inserts a newline. `Shift+Enter` keeps its native newline behavior in both modes.

```html
<script>
  window.__abEnterSends = false; // start in "Enter = new line" mode

  document.addEventListener('keydown', function (e) {
    if (e.isComposing) return;
    var isEnter = e.key === 'Enter' || e.key === 'NumpadEnter';
    if (isEnter && e.target && e.target.matches('textarea.ab-chat-surface__input') && !window.__abEnterSends) {
      e.stopPropagation(); // newline mode: neutralize native send; send mode: let it run
    }
  }, true);

  window.toggleEnterSends = function (enabled) {
    window.__abEnterSends = !!enabled;
    // update your UI indicator here
  };
</script>
```

```html
<button type="button" onclick="toggleEnterSends(true)">Enter = Send</button>
<button type="button" onclick="toggleEnterSends(false)">Enter = New Line</button>
```

## Workaround C — Modifier-based send (Ctrl+Enter)

Fold the modifier branch into the **same capture listener** as A1. A separate bubble-phase listener cannot work alongside A1/B: `stopPropagation()` in the capture phase halts the event before it ever bubbles back to `document`, so a bubble-phase modifier listener never runs.

```html
<script>
  // "Enter = new line, Ctrl/Cmd+Enter = send" in a single capture listener.
  document.addEventListener('keydown', function (e) {
    if (e.isComposing) return;
    var isEnter = e.key === 'Enter' || e.key === 'NumpadEnter';
    if (!isEnter || !e.target || !e.target.matches('textarea.ab-chat-surface__input')) return;

    if (e.ctrlKey || e.metaKey) {
      var form = e.target.closest('form'); // the composer EditForm (.ab-chat-surface__controls)
      var sendBtn = form && form.querySelector('.ab-chat-surface__submit[type="submit"]');
      if (sendBtn) {
        e.preventDefault();
        sendBtn.click();
      }
    }
    e.stopPropagation(); // send mode: prevents native double-fire; newline mode: blocks stray send
  }, true);
</script>
```

## IME composition caveat

Neither the library's native handler nor early drafts of these recipes check `e.isComposing`. During IME composition (CJK / Japanese / Korean input methods), pressing Enter confirms the candidate text; browsers then fire a follow-up `keydown` with `isComposing = false` and `key = 'Enter'`. Without a guard, that follow-up keypress sends the message (native behavior) or inserts a stray newline (A1/B newline mode). All recipes above guard with `if (e.isComposing) return;` — keep that guard when adapting them. (The library itself has the same gap; fixing it is a library change.)

## Placement & caveats

- Put these scripts in your **consumer app's** host page (e.g., a `wwwroot/app.js` referenced at the end of `<body>` in `App.razor`, or `_Host.cshtml`). A collocated `.razor.js` module also works when the page is interactive. **No package files are modified** — `_content/AgentBlazor/AgentBlazor.min.js` is read-only package output, and none of these recipes edit it.
- Scope selectors to `textarea.ab-chat-surface__input` — never `.ab-chat-surface__input` alone, or the clarification input (an `<input>`) is affected too.
- **Multi-instance**: A1/B/C are target-based (`e.target.matches(...)`), so they cover every chat surface on the page. A2's `querySelectorAll` also covers all instances — but a naive one-off `document.querySelector` patches only the first surface. Always use `querySelectorAll` (or `e.target.closest('form')` in C) so you never silently fix just one of several surfaces.
- `stopPropagation()` also suppresses Blazor's delegated `@onkeydown` for the intercepted key. Today the package's key handler only handles `Escape`, so nothing visibly breaks — but if the package ever handles Enter in .NET, these listeners would block it.
- **Stability**: A2 depends on `_abChatEnterHandler`, an internal property the package writes on the textarea — a future package version could rename or remove it. A1 (capture-phase listener) relies only on the stable DOM class and is the recommended default.
- **Update `Placeholder` when you flip the behavior**: the default is `"Type a message… (Enter to send, Shift+Enter for new line)"`, so a newline-mode composer would tell users the wrong thing.
- The send handler also attaches to the clarification input; the recipes above intentionally leave it on its default Enter-send behavior.
- These are **consumer-side only** — no library files are modified. If you need the toggle on `AgentChatSurface` itself (a real parameter forwarded by `AgentChatWidget`/`AgentChatPanel`, passed into `attachEnterSubmit`), that is a package change and out of scope here.
- If the composer text is also being retained after Send, fix the script loading first — see [`script-loading.md`](script-loading.md).
