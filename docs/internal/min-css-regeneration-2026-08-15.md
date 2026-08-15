# Shipped `AgentBlazor.min.css` Drift — Postmortem & Regeneration Pipeline

Created: 2026-08-15
Owner: AgentBlazor core team
Status: Resolved — pipeline in place, artifact regenerated, CI gate added
Related: [markdown-rendering-plan-2026-08-15.md](markdown-rendering-plan-2026-08-15.md) (finding #4 — same stylesheet family)

## Summary

`src/AgentBlazor.Components/wwwroot/AgentBlazor.min.css` — the un-scoped stylesheet
shipped in the `AgentBlazor` NuGet package and referenced by the public
`AgentBlazorAssetPaths.Css` constant (used by the CLI scaffolder, docs, demo,
starter sample) — had **silently fossilized**. It was a hand-maintained static
asset with no regeneration pipeline, and had drifted months behind the component
`.razor.css` sources.

## What was wrong (evidence)

- The source rule in `AgentChatSurface.razor.css` (~line 406) uses CSS variables:
  `color: var(--ab-chat-text)` / `font-size: var(--ab-chat-font-size)` (defaults
  `#0f172a` / `0.9375rem`, dark-theme `#f8fafc`).
- The shipped bundle contained a **literal fossil**:
  `.ab-chat-surface__item-text{color:#1f2933;font-size:.9rem}` — the values from
  the original pre-variable design.
- Provenance from git history:
  - `2550ce0` (2026-02-19) — original literals `#1f2933` / `0.9rem` in `razor.css`.
  - `cfbfba5` (2026-02-25, "Deterministic AG-UI agent, streaming, and UI
    overhaul") — source refactored to CSS variables; **`AgentBlazor.min.css`
    never updated**.
  - `1f33e95` (2026-04-17, "Fix shipped chat widget minimize styles") — last touch
    of the bundle; did not fix this rule.
- Magnitude of the drift: bundle was **8,751 chars**, covering only the old
  chat-surface/widget subset. The RCL has **13 `.razor.css` files** (shell,
  dashboard, inspector, generative UI ×5, chat bar/widget/panel/surface,
  markdown) — styles for ~12 component families were **missing** from the
  shipped un-scoped stylesheet.
- The variable `--ab-chat-text` was therefore **undefined** in the shipped
  bundle — consumers loading only `AgentBlazor.min.css` got a dangling
  `var()` (falls back to inherited page color) or, for missing components, no
  styles at all. The fingerprint (`AgentBlazor.min.bgbpv4z1ra.css`) masked this
  from consumers by design — no one could see the staleness.
- The unit test `PackagedCss_IncludesWidgetVisibilityStateRules` passed only
  because it was coupled to the **hand-written file's declaration order**.

## Root cause

A committed, hand-maintained artifact derived from compiler output, with:

1. **No regeneration pipeline** — nothing rebuilt the min file from the compiled
   scoped-CSS bundle (`obj/<tfm>/scopedcss/bundle/AgentBlazor.styles.css`).
2. **No freshness gate** — CI never compared the artifact to what a build
   produces.
3. **Tests coupled to artifact layout** — asserted exact declaration order, so
   they could never detect content drift (and would break a correct regeneration).

## The fix (implemented 2026-08-15)

1. **`scripts/regenerate-min-css.ps1`** — deterministic generator:
   - reads the Razor SDK's compiled scoped bundle (auto-discovers newest under
     `obj/`),
   - strips Blazor CSS-isolation selectors (`[b-<hash>]`) so the output remains
     the un-scoped "works everywhere" stylesheet (scope attrs never land on
     `MarkupString`/dynamic HTML),
   - string-aware minifies (comments + whitespace, safe for `calc()`/
     gradients/quoted font stacks; no space-before-colon pseudo-classes in repo),
   - writes UTF-8 no-BOM, **skips the write when unchanged** (stable
     timestamps/fingerprints),
   - `-Check` mode: exit 1 if committed file ≠ what sources produce.
2. **MSBuild target `RegenerateAgentBlazorMinCss`** in
   `AgentBlazor.Components.csproj` — `AfterTargets="BundleScopedCssFiles"`,
   `BeforeTargets="GenerateStaticWebAssetsManifest"`, so regeneration lands
   **before fingerprinting and pack**. `powershell.exe` on Windows, `pwsh` on CI.
   Design-time builds excluded.
3. **CI freshness gate** in `.github/workflows/ci.yml` — after Build,
   `git diff --exit-code -- src/AgentBlazor.Components/wwwroot/AgentBlazor.min.css`
   fails any PR that doesn't commit the regenerated file.
4. **Regenerated artifact** — 8,751 → 66,202 chars, now covering all 13
   component stylesheets; endpoint fingerprint rotated
   `AgentBlazor.min.bgbpv4z1ra.css` → `AgentBlazor.min.8w6pf6v0bb.css` (cache
   busting for existing consumers).
5. **Test hardened** — `PackagedCss_IncludesWidgetVisibilityStateRules` now
   asserts rule presence/content, not declaration order.
6. **Follow-up hardening (same session)** — base `.ab-chat-surface__item-text`
   gained fallbacks `var(--ab-chat-text, #0f172a)` / `var(--ab-chat-font-size, 0.9375rem)`
   (66,220 chars) so `AgentMarkdownContent` rendered **standalone** outside a
   `.ab-chat-surface` cannot dangle; mirrors the fallback pattern already used
   throughout `AgentMarkdownContent.razor.css`.

## Verification

- Build regenerates idempotently ("up to date", no write churn across TFMs).
- Demo app builds clean; manifest fingerprint proves regeneration happens before
  fingerprinting.
- `tests/AgentBlazor.Components.Tests`: **156 passed / 1 skipped** (pre-existing).
- `scripts/regenerate-min-css.ps1 -Check` exits 0.

## Lessons learned

1. **Any committed "min" asset derived from compiler output needs a regeneration
   pipeline + freshness gate** — without both, it will silently fossilize. The
   source was correct for 4 months while the shipped artifact rotted.
2. **Tests on generated artifacts must assert semantics** (rule exists, contains
   declarations), never artifact-specific declaration order or formatting.
3. **Static-web-asset fingerprinting masks staleness** — content changes were
   invisible to consumers by design; freshness must be enforced at build/CI, not
   discovered in the browser.
4. **Back-compat class names are load-bearing** — `ab-chat-surface__item-text`
   and `ab-chat-surface__item-text--markdown` are used by e2e selectors and
   consumer CSS; regeneration must preserve them (it does — they come from the
   markup, not the stylesheet).
5. **`var()` fallbacks are cheap insurance** — always add them where a rule's
   token-bearing ancestor is not guaranteed (standalone component usage).

## How to work with this going forward

1. Edit a `.razor.css` → the next build **regenerates**
   `wwwroot/AgentBlazor.min.css` automatically (before fingerprinting/packing).
2. **Commit both** the source change and the regenerated stylesheet — CI fails
   otherwise (`Verify AgentBlazor.min.css is committed fresh` step).
3. Manual one-off: `./scripts/regenerate-min-css.ps1`; local check:
   `./scripts/regenerate-min-css.ps1 -Check`.

## Deliberately not fixed

- Consumers caching the old 8.7 KB file — handled by fingerprint rotation.
- `demo/AgentBlazor.Demo.csproj` BOM + `UserSecretsId` churn — pre-existing,
   unrelated working-tree noise.
- File size (66 KB) — that is the *real* component CSS (previously ~92% was
   missing); it ships gzip-compressed via the static-web-assets pipeline.
