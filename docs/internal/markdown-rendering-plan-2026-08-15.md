# Chat Markdown Rendering Plan (Markdig + Mermaid)

Created: 2026-08-15
Owner: AgentBlazor core team
Status: Approved plan — Phase 1 (Foundation) + Phase 2 (Component + wiring) + Phase 3 (Client enhancement) + Phase 4 (Polish) implemented
Last updated: 2026-08-15

## Summary

Enhance chat timeline markdown rendering to be beautiful and complete: full
Markdig feature set, HTML sanitization, client-side mermaid diagram rendering,
and syntax highlighting. Markdig is already wired into `AgentChatSurface`; this
plan hardens it (sanitization), extracts it into a dedicated component, and adds
a client enhancement pass that runs once per final message (never mid-stream).

Architecture: **server-side Markdig → allowlist sanitizer (AngleSharp 1.2.0)
→ sanitized `MarkupString` → client-side enhance pass (lazy mermaid +
highlight.js) on final messages only.** This split is correct for this
codebase's hosting model (Blazor InteractiveServer with prerendering, streaming
text deltas).

The plan was refined after a rubber-duck critique; High-severity corrections
(sanitizer allowlist, mermaid-source protection, lock-file regeneration) are
baked into the phases below and preserved in the audit trail.

## Research Basis

- Markdig — fast, CommonMark 0.31.2-compliant, extensible markdown processor for
  .NET; `UseAdvancedExtensions()` includes `UseDiagrams()` (mermaid/nomnoml
  fences → `<div class="mermaid">`) and `UseMathematics()`:
  <https://github.com/xoofx/markdig>, <https://xoofx.github.io/markdig>
- Markdig extension surface (`MarkdownExtensions.cs`) — confirms
  `UseDiagrams()`, `DisableHtml()`, `UseAlertBlocks()`, `UseCjkFriendlyEmphasis()`:
  <https://github.com/xoofx/markdig/blob/main/src/Markdig/MarkdownExtensions.cs>
- Ganss.Xss HtmlSanitizer — established Blazor `MarkupString` sanitizer; **the
  `class` attribute is disallowed by default** (classjacking prevention) and
  must be explicitly allowed; re-serializes via AngleSharp, so text content is
  not preserved byte-for-byte:
  <https://github.com/mganss/HtmlSanitizer>, <https://xss.ganss.org/>
  **Evaluated and rejected in Phase 1** — no published version is compatible
  with AngleSharp `1.2.0` (bunit 1.40.0 requirement; see deviation in Phase 1,
  step 2). Its design (parse → allowlist walk → re-serialize) was replicated by
  the in-repo `AgentHtmlSanitizer`.
- Blazor raw-HTML guidance — `MarkupString` must only receive sanitized HTML:
  <https://blazor.syncfusion.com/documentation/common/security/cross-site-scripting-prevention>,
  <https://blog.emilianomontesdeoca.com/posts/blazor-markup-string-raw-html/>

## Current State (verified 2026-08-15)

- `src/AgentBlazor.Components/Chat/AgentChatSurface.razor` lines ~476–482:
  static `_mdPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build()`
  and `RenderMarkdown()` returning `MarkupString`. Used for:
  - timeline assistant/proactive items (line ~80),
  - the streaming bubble `_streamingResponseText` (lines 226–228) — re-rendered
    on **every** `TextDelta` (line ~1240 `_streamingResponseText += streamEvent.TextDelta;`
    then `StateHasChanged`).
- Final streamed response becomes a timeline item at line ~1391
  (`_timeline.Add(ChatTimelineItem.Agent(...))`), after `_streamingResponseText`
  is cleared at line ~1290.
- **No HTML sanitization anywhere** — no `DisableHtml()`, no sanitizer library.
  LLM output flows raw into `MarkupString` (prompt-injection / XSS surface).
- **No mermaid rendering, no syntax highlighting** in the UI today.
- Markdig `0.38.0` referenced from `Directory.Packages.props`
  (`src/AgentBlazor.Components/AgentBlazor.Components.csproj`). Components targets
  `net8.0;net9.0;net10.0`. MudBlazor 9.0.0.
- CSS: scoped `AgentChatSurface.razor.css` lines ~934–1000 ("Markdown Rendering"
  block). **This CSS is dead code** — the compiled bundle scopes to the last
  element inside the `MarkupString` (e.g. `h1[b-scope]`), and raw markup never
  receives scope attributes. Markdown in the demo is currently unstyled.
- Shipped JS: hand-authored global `window.AgentBlazor` in
  `wwwroot/AgentBlazor.min.js` (no build/minification pipeline), loaded eagerly
  via `<script>` in the host page through `AgentBlazorAssetPaths.Js`. Interop is
  fire-and-forget `JSRuntime.InvokeVoidAsync("AgentBlazor.*", ...)` in try/catch.
- Repo: `Directory.Build.props` sets `RestorePackagesWithLockFile=true` and
  `RestoreLockedMode` in CI → **all `packages.lock.json` must be regenerated**
  when Markdig bumps (it is a transitive dependency of every project referencing
  Components).
- Demo hosting: InteractiveServer render mode with prerendering.
- Tests: bunit `TestContext`; `AgentChatSurfaceTests` use default Strict
  JSInterop (calls are try/catch-swallowed); `CompatibilityRenderParityTests`
  uses `JSInterop.Mode = Loose`.

## Key Design

- Keep **server-side Markdig** (correct for server-rendered + prerendered +
  streaming; client-side markdown-it would break prerender).
- Pipeline: `UseAdvancedExtensions().DisableHtml()` — raw HTML from the model is
  escaped, not executed. Sanitize the Markdig HTML output with the in-repo
  `AgentHtmlSanitizer` (AngleSharp 1.2.0 allowlist walk; see Phase 1 steps 2–3).
- **Mermaid source protection**: extract mermaid block contents before
  sanitizing and re-insert HTML-escaped afterwards — AngleSharp re-serialization
  would otherwise corrupt diagram source (`A["<b>Bold</b>"]` labels get parsed
  as real elements).
- New child component `AgentMarkdownContent` owns rendering + enhancement;
  markdown CSS moves there with `::deep` so it actually applies to
  `MarkupString` content (scoped attrs never land inside raw markup).
- Client enhance pass (`AgentBlazor.markdown.enhance`) runs **only when
  `Enhance=true`** → timeline items only, never the streaming bubble. Final
  message sequencing is safe: bubble cleared (line ~1290) before timeline add
  (line ~1391), flushed by the post-handler render.
- Delivery: **CDN default + configurable URLs via `MarkdownOptions`** — bundling
  mermaid (≈1 MB gzipped) into the nupkg would roughly double it for every
  consumer; local `wwwroot/` files remain available as a self-hosted fallback.

## Rubber-Duck Review Findings (audit trail, 2026-08-15)

A devil's-advocate subagent verified every claim against code, compiled output,
and package metadata. Verdict: architecture sound; three High defects corrected.

| # | Sev | Finding (verified) | Resolution in this plan |
|---|---|---|---|
| 1 | High | `class` is **not** default-allowed by Ganss.Xss (classjacking prevention). Markdig output relies on `class` for `<div class="mermaid">`, `language-*` code, task lists, alert blocks. | Explicitly add `class` to sanitizer allowlist; reject `id`, `style`, `target`, `iframe`. |
| 2 | High | Sanitizer (AngleSharp) **corrupts mermaid source** inside `.mermaid` divs — not preserved byte-for-byte. | Extract diagram text pre-sanitize; re-insert HTML-escaped post-sanitize. Unit-test with sequence diagram + HTML-in-label flowchart. |
| 3 | High | Repo-wide `packages.lock.json` + CI `RestoreLockedMode`; Markdig is transitive everywhere → CI red on day one if lock files aren't regenerated. | Explicit Phase 1 step: solution-wide restore, commit all lock-file diffs. |
| 4 | Med | Current markdown CSS is **dead code** (scope attr lands on last element inside `MarkupString`). | Greenfield styling in `AgentMarkdownContent.razor.css` using `::deep`; keep wrapper class `.ab-chat-surface__item-text--markdown` for consumer back-compat. |
| 5 | Med | Sanitize-per-delta adds AngleSharp cost on top of Markdig-per-delta during streaming. | Decision: sanitize always + coalesce streaming re-renders (~100 ms); add micro-benchmark. |
| 6 | Med | Final-message enhance relies on implicit post-handler render; a presence-flag guard goes stale if Blazor re-diffs content. | Guard = content hash (`data-ab-processed`), not a boolean. bunit test asserts exactly one enhance per final message. |
| 7 | Med | Local bundling of mermaid + hljs doubles the nupkg for all consumers. | Flip to CDN default + configurable URLs; local files only as fallback. |
| 8 | Med | Sanitization is a behavior change: YouTube/Vimeo iframes, `data:` image URIs, `mailto:` autolinks stop working. | Document in release notes + `PACKAGE_README.md`; `MarkdownOptions.Sanitize` default `true` with opt-out. |
| 9 | Low | `target="_blank"` tabnabbing via attributes extension. | Disallow `target` in sanitizer (or add `rel="noopener noreferrer"`). |
| 10 | Low | Markdig 0.38 → 1.3.2 real and TFM-safe, but render drift (e.g. pipe-only separator rows). | Golden-HTML snapshot tests for tables/alerts/task lists/mermaid. |
| 11 | Low | Render-mode matrix: Static SSR never renders diagrams; `display:none` hosts get zero-size SVGs; widget is `visibility:hidden` (OK). | Document mermaid requires interactive rendering; consider gating on `offsetParent`/IntersectionObserver. |
| 12 | Low | Async mermaid changes height after `scrollToBottom` runs → last diagram cut off. | `InvokeAsync` with a promise; re-request scroll after enhance completes. |
| 13 | Low | Mermaid SVG unlabeled; animations ignore reduced motion. | `role="img"` + `aria-label`; `prefers-reduced-motion` CSS. |
| 14 | Low | bunit Strict JSInterop swallows try/catch calls → nothing recorded. | New tests use `JSInterop.Mode = Loose` + `WaitForAssertion`. |
| 15 | Low | Release hygiene: `PackageReleaseNotes`, `PACKAGE_README.md`, demo showcase, e2e. | Fold into Phase 4; add Playwright mermaid→SVG check. |

## Implementation Plan

### Phase 1 — Foundation (dependencies: none)

1. ✅ Bump `Markdig` `0.38.0` → `1.3.2` in `Directory.Packages.props`
   (adds GitHub-style Alerts, CJK-friendly emphasis, pipe-table perf; API surface
   used here is stable across the jump).
2. ✅ Pin `AngleSharp` `1.2.0` in `Directory.Packages.props` + add a direct
   reference in `src/AgentBlazor.Components/AgentBlazor.Components.csproj`.
   **Deviation from plan:** the original plan called for `HtmlSanitizer`
   (Ganss.Xss) `9.1.974`. That package was rejected during implementation: every
   published Ganss.Xss version requires either ancient AngleSharp (0.16/0.17)
   or modern (1.6/1.7), and none is compatible with AngleSharp `1.2.0` which
   bunit 1.40.0 (the latest bunit, already in the test graph) is compiled
   against — adopting Ganss.Xss would permanently break the test project
   (`MissingMethodException` in `Bunit.RefreshableElementCollection`).
   Replacement: a minimal, conservative allowlist sanitizer
   (`AgentHtmlSanitizer`) built directly on AngleSharp `1.2.0` (which was
   already in the dependency graph via bunit) — see step 3.
3. ✅ New `src/AgentBlazor.Components/Markdown/AgentMarkdownRendering.cs` +
   `src/AgentBlazor.Components/Markdown/AgentHtmlSanitizer.cs`:
   - pipeline `UseAdvancedExtensions().DisableHtml()`,
   - singleton `AgentHtmlSanitizer` (AngleSharp DOM walk): allowlist **adds
     `class`**, **rejects `id`, `style`, `target`**, drops `script`/`iframe`/
     `style`/`link`/`meta`/`object` subtrees, strips all `on*` handlers,
     allows only `http`/`https`/`mailto` on `href` and `http`/`https` on `src`
     (relative URLs kept — same-origin), keeps `input[type=checkbox]`
     `[checked][disabled]` (task lists),
   - **mermaid/nomnoml extract-and-reinsert** around sanitization (source body
     HTML-escaped so AngleSharp serialization can't corrupt diagram labels),
   - exposes `Render(string?)` / `RenderMarkup(string?)`.
4. ✅ Unit tests `tests/AgentBlazor.Components.Tests/Markdown/AgentMarkdownRenderingTests.cs`
   (17 tests): mermaid fence → `<div class="mermaid">`; sequence diagram +
   HTML-in-label flowchart source survives sanitization; `<script>`/`onclick`/
   `javascript:`/`data:` stripped (links **and** image `src`); raw HTML escaped
   via `DisableHtml`; `mailto:` autolink preserved; task lists preserved;
   structural assertions for tables/alerts/task lists/mermaid (golden-HTML
   snapshots deferred to the golden-snapshot phase in the plan).
5. ✅ **Regenerate all `packages.lock.json`** (solution-wide restore with
   `--force-evaluate`) — required for CI `RestoreLockedMode`; verified no
   `HtmlSanitizer` remnants remain. Note: `**/packages.lock.json` is
   gitignored (`.gitignore:494`), so regeneration is local-only; nothing to
   commit.

### Phase 2 — Component + wiring (depends on Phase 1)

6. ✅ New `src/AgentBlazor.Components/Chat/AgentMarkdownContent.razor`
   (+ `.razor.css`, code-behind `AgentMarkdownContent.razor.cs`): params
   `Content`, `Enhance`, optional `Class`; `OnAfterRenderAsync` invokes
   `AgentBlazor.markdown.enhance(element, options)` in try/catch **only when
   `Enhance=true`**. Server-side idempotency guard (`_lastEnhancedContent`)
   guarantees exactly one enhance call per distinct Content value even when
   the parent re-renders (mirrors the Phase 3 client content-hash guard).
   Wrapper classes unchanged (`ab-chat-surface__item-text
   ab-chat-surface__item-text--markdown`) for back-compat with e2e selectors.
7. ✅ `AgentChatSurface.razor`:
   - timeline items → `<AgentMarkdownContent Content="@item.Text" Enhance />`,
   - streaming bubble → `<AgentMarkdownContent Content="@_streamingResponseText" />`
     (no enhance → no diagram/highlight thrash mid-stream; final message is a
     timeline item, so diagrams render once at the end),
   - deleted old `_mdPipeline` / `RenderMarkdown` and `@using Markdig`.
8. ✅ **Greenfield** CSS in `AgentMarkdownContent.razor.css` with `::deep`
   (e.g. `.ab-chat-surface__item-text--markdown ::deep h1`): refined headings,
   code-block panel, tables, blockquote, task-list checkboxes (`:has(:checked)`
   strikethrough), GitHub-style alerts (note/tip/important/warning/caution),
   `.mermaid`/`.nomnoml` centering + `overflow-x: auto`, links/hr/images —
   all consuming `--ab-chat-*` tokens with fallbacks, respecting
   `prefers-reduced-motion`. Code-block **language label** deferred to the
   Phase 3 client pass (CSS can't cleanly strip the `language-` prefix).
   The dead markdown block in `AgentChatSurface.razor.css` was deleted.
   Visual smoke check required (no unit test asserts CSS application).
9. ✅ bunit tests `AgentMarkdownContentTests.cs` (10 tests): sanitized
   `MarkupString` renders (headings/bold, mermaid div, script escaped); JS
   invoked when `Enhance=true` exactly once, not when false; same-content
   re-render does not re-enhance; content change enhances again;
   Strict-mode missing JS swallowed without crash; `Class` param applied.

### Phase 3 — Client enhancement (depends on Phase 2)

10. ✅ `wwwroot/AgentBlazor.min.js` — new `AgentBlazor.markdown.enhance(containerEl, options)`
    namespace:
    - lazy load of mermaid (only if `.mermaid`/`.nomnoml` present) and
      highlight.js (only if `pre code[class*="language-"]`),
    - **Deviation:** lazy loading uses **script-tag injection** (UMD builds)
      instead of dynamic `import()`. Empirically verified during the feedback
      loop: highlight.js v11 ships only Node CJS (`lib/*`) and a browser-unusable
      ESM (`es/*` re-exports the CJS build → browsers throw "does not provide an
      export named 'default'"), and UMD `build/` bundles were dropped in v11.
      cdnjs serves the real UMD `highlight.min.js` (all languages) and jsdelivr
      serves mermaid `dist/mermaid.min.js` (UMD, sets `window.mermaid`). Loads at
      most once (module cache), script failure rejects → fallback keeps raw text.
    - `mermaid.initialize({ startOnLoad: false, theme, securityLevel: 'strict',
      suppressErrorRendering: true })` once, then per-node
      `mermaid.render()` (per-node so one bad diagram can't kill the rest),
    - theme read from closest `data-theme` (dark → `dark`, else `default`),
    - **content-hash guard**: `data-ab-processed` = `sourceHash` passed by the
      component (stable hash of the markdown SOURCE, not the DOM — the DOM is
      mutated by enhancement, so textContent hashing would mismatch every
      re-render; found + fixed by the smoke test in the feedback loop),
    - `role="img"` + `aria-label="Diagram: mermaid|nomnoml"` on rendered SVG,
    - graceful fallback: failed diagram leaves the raw source text visible,
    - returns a Promise (caller can re-request `scrollToBottom`).
11. ✅ `MarkdownOptions` (`src/AgentBlazor.Components/Chat/MarkdownOptions.cs`,
    namespace `AgentBlazor.Components.Chat`, matching `ChatTheme`):
    `EnableMermaid`/`EnableSyntaxHighlighting` (default true),
    `MermaidScriptUrl`/`HighlightScriptUrl` (default = verified CDN URLs above),
    `Sanitize` (default true → skips the `AgentHtmlSanitizer` pass when false;
    raw model HTML is still escaped by `DisableHtml()`). Added to
    `AgentChatSurface`, threaded through `AgentChatWidget`/`AgentChatPanel`,
    serialized into the `enhance` call (incl. `sourceHash`). Verified: 7 new
    bunit tests (options serialization, sanitize toggle incl. a real
    generic-attributes `style` vector + auto-identifier `id`, surface/widget/
    panel passthrough) + isolated Playwright spec
    `tests/e2e/specs/markdown-enhance.spec.cjs` (4 tests, run via
    `npm run test:markdown-enhance`, no demo server needed) + one-off smoke
    script `tests/e2e/scripts/markdown-enhance-smoke.cjs` (11 checks).

### Phase 4 — Polish (independent)

12. ✅ **Copy button on code blocks** — `MarkdownOptions.EnableCodeCopy` (default
    true) → `_wireCopyButtons` in `AgentBlazor.min.js`: wraps each `pre` with
    a ghost `.ab-copy-btn` (hover-revealed, `:focus-visible` + touch always
    visible, `data-copied` flash). Copies `pre code` text via
    `navigator.clipboard.writeText` with an `execCommand` fallback.
    **Bug found by the demo e2e:** Markdig 1.3.2 emits diagrams as
    `<pre class="mermaid">` (NOT `<div>`), and `_wireCopyButtons` selected all
    `pre` — the button was appended to diagram containers synchronously
    BEFORE the async mermaid script resolved, so the diagram source gained a
    trailing "Copy" line → parse error → diagrams silently failed (the short
    graph only "worked" because `A --> BCopy` is accidentally valid syntax).
    Fixed: copy wiring skips `pre.mermaid`/`pre.nomnoml`, and both diagram
    renderers snapshot their sources synchronously at call time (defense in
    depth). Regression test added.
13. ✅ **Demo showcase page** — `demo/AgentBlazor.Demo/Components/Pages/Demo/MarkdownShowcase.razor`
    (`/demo/markdown-showcase`, DemoLayout, dark bubble so `--ab-chat-*` tokens
    apply verbatim): headings, lists, task list, table, all five GitHub
    alerts, 4 fenced code blocks (csharp/js/bash/json), 2 mermaid diagrams
    (graph TD + sequenceDiagram), 1 nomnoml diagram, raw-source `<details>`.
    Surfaced on the demo home (`/demo`) as a static "Markdown showcase" card
    (no agent required).
14. ✅ **e2e** — two specs, both green:
    - `tests/e2e/specs/markdown-enhance.spec.cjs` (9 tests, isolated, no
      server): mermaid/nomnoml → SVG + a11y, copy + clipboard shim, guard
      idempotency (incl. no-sourceHash fallback), disable flags, and the
      Markdig-style `<pre class="mermaid">` regression (renders + no copy
      button). `npm run test:markdown-enhance`.
    - `tests/e2e/specs/markdown-demo.spec.cjs` (3 tests, boots the REAL demo
      app in Development on a fixed port, `reuseExistingServer: false` —
      `dotnet run` can orphan a child process, and a random port differs per
      config evaluation): full-server render of the showcase (2 mermaid SVGs
      + nomnoml + hljs + copy buttons), clipboard copy, launchpad link.
      `npm run test:markdown-demo`.
    - **Nomnoml is a split bundle**: its UMD does
      `e(t.nomnoml={}, t.graphre)` — graphre must load first (jsdelivr
      `graphre@0.1.3/dist/graphre.js`; cdnjs does NOT host graphre), then
      nomnoml 1.7.0 (cdnjs UMD, `window.nomnoml.renderSvg`). Both chained in
      `_loadNomnoml`; `NomnomlScriptUrl` overrides only the nomnoml half.
    - Mermaid renders are executed **sequentially** (not `Promise.all`):
      parallel `mermaid.render` calls on one `initialize()` raced and one
      diagram silently failed on a two-diagram page.
15. ✅ `PACKAGE_README.md` — new "Markdown rendering in chat" section
    (pipeline → sanitizer → enhance pass, `MarkdownOptions` example,
    CDN/self-hosted overrides, theme sync). Release notes:
    `docs/releases/0.2.24-internal.1.md` (sanitizer behavior change,
    `MarkdownOptions` surface, nomnoml/graphre loading, copy buttons).

## Recorded Decisions

- **Sanitize always + coalesce streaming re-renders (~100 ms)** — NOT
  sanitize-only-final (the transient raw-HTML window is exploitable).
- **CDN default for mermaid/highlight.js**, URLs configurable via
  `MarkdownOptions`; local `wwwroot/` files as self-hosted fallback.
- **User messages stay plain text** (current behavior; Blazor escapes).
- **`AgentChatBar` and `AgentRemoteChatSurface` excluded** — both render plain
  text today, no markdown path.
- **Mermaid theme syncs with `data-theme`** (in core scope — cheap, prevents
  ugly diagrams). **KaTeX math deferred** (out of scope unless agents output
  LaTeX).
- **Markdig 1.3.2 upgrade kept** — smaller 0.40.x bump would forfeit Alerts.
- Sanitizer behavior changes are documented release-note items; `Sanitize`
  opt-out available via `MarkdownOptions`.

## Out of Scope

- KaTeX math rendering (deferred).
- Markdown for user messages, `AgentChatBar`, `AgentRemoteChatSurface`.
- Changing the existing global-script delivery pattern.
- Mermaid/`data:`/`mailto:`/iframe re-enablement beyond documented opt-outs.

## Security & Compatibility Notes

- `DisableHtml()` + sanitizer kill the current raw-LLM-HTML XSS surface.
- `class` must be allowed or every enhancement hook breaks (defect 1).
- Mermaid source must bypass sanitization via extract-and-reinsert (defect 2).
- CSP: mermaid injects a `<style>` element at runtime — consumers with
  `style-src` without `'unsafe-inline'` get broken diagrams; document.
- Behavior change: YouTube/Vimeo embeds, `data:` images, `mailto:` autolinks
  stop rendering (see Recorded Decisions).

## Verification Plan

1. `dotnet build` + `dotnet test` (`AgentBlazor.sln`) — new unit + bunit tests
   green (golden HTML snapshots included); existing `AgentChatSurfaceTests` /
   `CompatibilityRenderParityTests` unaffected.
2. Demo run (InteractiveServer + prerender): prompt with tables, task lists,
   fenced code, a ` ```mermaid ` flowchart → SVG after stream completes (not
   mid-stream), code highlighted, no console errors.
3. Security probes: `<script>alert(1)</script>`, `[click](javascript:alert(1))`,
   `data:` image → inert text / disabled link / stripped.
4. Broken mermaid syntax → suppressed error, no crash.
5. Refresh (history hydration) → diagrams re-render once (content-hash guard),
   no double-render.
6. e2e Playwright: mermaid → SVG assertion.
7. Micro-benchmark of Markdig + AngleSharp per delta to validate the ~100 ms
   streaming coalesce decision.

## Relevant Files

- `src/AgentBlazor.Components/Chat/AgentChatSurface.razor` — `RenderMarkdown`/
  `_mdPipeline` (476–482), timeline items (~80), streaming bubble (226–228),
  final timeline add (~1391)
- `src/AgentBlazor.Components/Chat/AgentChatSurface.razor.css` — dead markdown
  block (934–1000)
- `src/AgentBlazor.Components/Chat/AgentChatWidget.razor` /
  `AgentChatPanel.razor` — param passthrough
- `src/AgentBlazor.Components/wwwroot/AgentBlazor.min.js` — `markdown` namespace
  (hand-authored, no build pipeline)
- `Directory.Packages.props`, `src/AgentBlazor.Components/AgentBlazor.Components.csproj`
- New: `src/AgentBlazor.Components/Markdown/AgentMarkdownRendering.cs` (+
  `AgentHtmlSanitizer.cs`), `src/AgentBlazor.Components/Chat/AgentMarkdownContent.razor`
  (+ `.razor.css`, + `.razor.cs`), `src/AgentBlazor.Components/Chat/MarkdownOptions.cs`,
  tests under `tests/AgentBlazor.Components.Tests/`, e2e under
  `tests/e2e/specs/markdown-enhance.spec.cjs` + `markdown-enhance.config.cjs`,

## Related Follow-up

- **2026-08-15 — shipped `AgentBlazor.min.css` regeneration pipeline** (postmortem
  + lesson learned): [min-css-regeneration-2026-08-15.md](min-css-regeneration-2026-08-15.md).
  The un-scoped stylesheet shipped via `AgentBlazorAssetPaths.Css` had fossilized
  (literal `#1f2933`, missing ~12 component families). Now auto-regenerated from
  the compiled scoped-CSS bundle on every build (MSBuild hook before
  fingerprinting), with a CI freshness gate and a hardened, order-independent
  `PackagedCss` test. The `ab-chat-surface__item-text` /
  `ab-chat-surface__item-text--markdown` back-compat classes (finding #4, e2e
  selectors) are preserved — they come from the markup, not the stylesheet.
  `tests/e2e/specs/markdown-demo.spec.cjs` + `markdown-demo.config.cjs`
- New (Phase 4): `demo/AgentBlazor.Demo/Components/Pages/Demo/MarkdownShowcase.razor`
  (+ `.razor.css`), `docs/releases/0.2.24-internal.1.md`
- All `**/packages.lock.json` (regenerate in Phase 1)

## Follow-up Status Tracker

| Phase | Status | Started | Completed | Notes |
|---|---|---|---|---|
| 1 — Foundation | ✅ completed | 2026-08-15 | 2026-08-15 | Markdig 1.3.2, AngleSharp 1.2.0 sanitizer (see deviation, step 2), renderer, 17 unit tests, lock files regenerated |
| 2 — Component + wiring | ✅ completed | 2026-08-15 | 2026-08-15 | AgentMarkdownContent (+code-behind), `::deep` CSS, surface wiring, 10 bunit tests, dead CSS removed |
| 3 — Client enhancement | ✅ completed | 2026-08-15 | 2026-08-15 | `markdown.enhance` JS (script-tag lazy load — see deviation), MarkdownOptions threading, 7 bunit + 4 e2e + 11 smoke checks |
| 4 — Polish | ✅ completed | 2026-08-15 | 2026-08-15 | copy button + `pre.mermaid` bug fix, demo showcase page, 9 isolated + 3 live-server e2e, README + release notes |

When a phase starts, flip its Status to `in progress` and add the date. When it
finishes, record the completion date and link any e2e/validation report paths
under `tests/e2e/artifacts/`. Keep this doc's `Last updated` in sync.
