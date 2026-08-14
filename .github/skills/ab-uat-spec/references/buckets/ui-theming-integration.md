# UAT Bucket — UI Theming & Integration

> Aspect: AgentBlazor/MudBlazor coexistence with other UI libraries, theming, CSS isolation (consumer app).
> Status: **SCAFFOLD** — no test cases yet. Populate incrementally as `ab-ui-integration`
> and `ab-mud-components` work lands.
> **Prefix:** `THEM-###` (bucket-local sequence; scaffolds start at `THEM-001` as cases land).
> Shared prerequisites: see `../runbook.md` once cases land (boot, personas, auth, evidence).

## Intended scope

Cases here assert that AgentBlazor renders correctly inside the Lifeline surface without
breaking (or being broken by) the host's existing UI:

- **UI coexistence** (`ab-ui-integration`): AgentBlazor alongside another component library
  (BlazorBlueprint on Lifeline) with no class-name/style conflicts, no CSS leakage.
- **Theming** (`ab-mud-components`): `AgentThemeProvider`, theme attributes, layout/shell
  providers (`AgentBlazorShell`, `AgentPopoverProvider`, `AgentDialogProvider`).
- **Shell/overlay/motion** (`lifeline-uxui`-style browser-proofing): overlays, dialogs,
  drawer, animation correctness.

## Example case shapes (draft — populate when the feature is exercised)

- Coexistence smoke: the chat surface and a host-native component render on the same page
  with no console CSS warnings and no layout overflow.
- Theming: applying the theme provider propagates the expected palette to AgentBlazor
  components without disturbing host styles.
- Shell: popover/dialog providers render overlays at correct z-index without clipping.

> Placeholder — replace with concrete GIVEN/WHEN/THEN cases as feature work lands.
