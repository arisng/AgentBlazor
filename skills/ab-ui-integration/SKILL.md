---
name: ab-ui-integration
description: "Integrate AgentBlazor (with its MudBlazor dependency) into a Blazor project that already uses another UI component library — Telerik, Radzen, Syncfusion, DevExpress, Blazorise, Ant Design Blazor, or custom CSS — without class-name or style conflicts. Triggers: \"CSS conflict\", \"CSS clash\", \"MudBlazor conflict\", \"coexist with [library]\", \"add AgentBlazor to existing app\", \"integrate AgentBlazor with [library]\", \"prevent CSS leaking\", \"CSS isolation with AgentBlazor\", \"AgentBlazor and MudBlazor setup\", \"style collision\", \"component library conflict\", \"avoid MudBlazor breaking my UI\"."
---

# `ab-ui-integration` — Conflict-Free UI Integration

## Conflict Surface

AgentBlazor depends on MudBlazor internally (12 component wrappers inherit from MudBlazor classes; 5 rendering components use MudBlazor markup). Three things flow into the host page:

| Asset | Source | Potential conflict |
|---|---|---|
| `MudBlazor.min.css` | MudBlazor NuGet | `mud-*` classes (inert if unused) |
| `MudBlazor.min.js` | MudBlazor NuGet | Targets only `mud-*` DOM elements |
| `AgentBlazor.min.css` | AgentBlazor NuGet | `ab-*` classes only |
| `AgentBlazor.min.js` | AgentBlazor NuGet | `window.AgentBlazor` only |

## Isolation Guarantees

These mechanisms **guarantee zero CSS/JS conflicts** with any other library:

| Mechanism | Scope | Details |
|---|---|---|
| **BEM namespace** | CSS classes | Every class starts with `ab-` (chat) or `mud-` (MudBlazor). Zero overlap with `k-*`, `rz-*`, `dx-*`, `bs-*`, `ant-*` |
| **CSS variable prefix** | Custom properties | All variables use `--ab-*` prefix. No collision with `--mud-*`, `--bs-*`, or any other token system |
| **Blazor CSS isolation** | `.razor.css` files | Compiler appends `b-<hash>` attribute selectors — bulletproof scoping |
| **Data-attribute theming** | HTML attributes | AgentBlazor uses `data-theme`, `data-style`, `data-radius` on its own root elements only |
| **JS namespace** | Browser globals | Single `window.AgentBlazor` object — no global pollution |
| **No global resets** | CSS cascade | AgentBlazor applies **zero** global reset styles (no box-sizing, no margin resets, no font changes at `:root` or `body`) |

## Prerequisites

The host project **must** have MudBlazor registered — even if you never use MudBlazor components directly:

```bash
dotnet add package MudBlazor
```

```csharp
// Program.cs
using MudBlazor.Services;
// ...
builder.Services.AddMudServices();
```

MudBlazor's service registration (`AddMudServices`) registers its own infrastructure (Snackbar, Dialog, ResizeListener, etc.) using service-keys and internal patterns that do **not** interfere with any other DI registration.

## Integration Workflow

### Step 1 — CSS loading order in `App.razor`

```razor
<head>
    <!-- 1. MudBlazor (AgentBlazor dependency) -->
    <link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
    <!-- 2. Your existing library (Telerik, Radzen, Syncfusion, etc.) -->
    <link rel="stylesheet" href="@Assets["_content/Telerik.UI/blazor.css"]" />
    <!-- 3. AgentBlazor (namespaced ab-* styles) -->
    <link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
    <!-- 4. Your app styles -->
    <link rel="stylesheet" href="@Assets["app.css"]" />
</head>
```

The ordering between #1 and #2 doesn't matter for conflict because `mud-*` and `k-*`/`rz-*` are different namespaces. Ordering #3 after #2 just ensures consistency.

### Step 2 — JS loading order in `App.razor`

```razor
<body>
    <script src="@Assets["_framework/blazor.web.js"]"></script>
    <!-- 1. MudBlazor JS -->
    <script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
    <!-- 2. AgentBlazor JS -->
    <script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
</body>
```

### Step 3 — Add MudBlazor infrastructure providers in your layout

MudBlazor requires four provider components somewhere in the render tree. They must be present for AgentBlazor's dialog/snackbar/popover features. **They do not affect your existing layout styling:**

```razor
@* Your layout — unchanged except adding these lines *@
<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<div class="page">
    @* Your existing content, using whatever library *@
    @Body
</div>
```

If `MudThemeProvider` is not configured (no `Theme` parameter), it uses MudBlazor defaults — which only affect elements with `mud-*` classes (i.e., MudBlazor/AgentBlazor internal UI only).

### Step 4 — Wrap with `AgentBlazorShell`

```razor
@using AgentBlazor.Components

@* In your layout *@
<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<AgentBlazorShell>
    <div class="page">
        @Body
    </div>
</AgentBlazorShell>
```

`AgentBlazorShell` renders `<section class="ab-shell">` and the floating `AgentChatWidget`. It does **not** add global resets, margins, or layout constraints to any ancestor or sibling elements.

## Scenario-Specific Guidance

### A — Project already uses MudBlazor

No conflict at all. AgentBlazor reuses the same MudBlazor version from `Directory.Packages.props`. You already have `AddMudServices()`, the providers, and the CSS/JS. Just add:

1. `@using AgentBlazor.Components` to `_Imports.razor`
2. `AgentBlazorAssetPaths.Css` and `AgentBlazorAssetPaths.Js` to `App.razor`
3. `AgentBlazorShell` around your layout content
4. `services.AddAgentBlazor(...)` + `app.MapAgentBlazorEndpoints()` in `Program.cs`

### B — Project uses a different component library (Telerik, Radzen, Syncfusion, DevExpress, Blazorise, Ant Design Blazor, etc.)

Follow all four steps above. Specific notes per library:

| Library | CSS namespace | MudBlazor compatibility |
|---|---|---|
| **Telerik UI for Blazor** | `.k-*`, `.kendo-*` | Fully independent |
| **Radzen Blazor** | `.rz-*` | Fully independent |
| **Syncfusion Blazor** | `.e-*`, `.ej-*` | Fully independent |
| **DevExpress Blazor** | `.dx-*` | Fully independent |
| **Blazorise** | `.b-*` | Fully independent |
| **Ant Design Blazor** | `.ant-*` | Fully independent |
| **Bootstrap** (via any wrapper) | `.bs-*`, `.btn`, `.container`* | MudBlazor uses `mud-*` only. If your Bootstrap imports also define resets at `body`, `*`, or `:root`, they may affect AgentBlazor's scoped subtree. Mitigate by inserting MudBlazor CSS *before* Bootstrap CSS so Bootstrap's cascade wins on shared properties. |

> \* Bootstrap's global resets on `*`, `body` apply universally. MudBlazor and AgentBlazor components are designed to handle typical CSS reset properties (box-sizing, margin) correctly. Test if you see visual regressions — typically none.

### C — Project uses only custom/semantic CSS

Same as Scenario B. Your custom CSS classes (whatever naming convention) cannot collide with `ab-*` or `mud-*` namespaces.

## Verification Checklist

After integration, confirm these statements are true:

- [ ] Your existing pages render identically before and after adding AgentBlazor
- [ ] AgentBlazor chat widget opens/closes and renders correctly
- [ ] Your existing library's modals/dropdowns/popups still work
- [ ] MudBlazor's `MudPopoverProvider` isn't clipping your other library's overlays (if so, adjust z-index: AgentBlazor widget uses `z-index: 2000`)
- [ ] No duplicate `AddMudServices()` calls in `Program.cs`
- [ ] `app.MapAgentBlazorEndpoints()` is present
- [ ] No `@using MudBlazor` in your existing components unless you actually use MudBlazor components there

## Common Pitfalls

| Symptom | Likely cause | Fix |
|---|---|---|
| Chat widget doesn't appear | Missing `AgentBlazorShell` wrapper | Wrap layout content in `<AgentBlazorShell>` |
| Agent responses hang | Missing `app.MapAgentBlazorEndpoints()` | Add after `app.MapRazorComponents<App>()` |
| MudBlazor snackbar/dialog not working | Missing MudBlazor providers | Add `<MudSnackbarProvider>` / `<MudDialogProvider>` in layout |
| Existing library popups clipped behind chat widget | Z-index conflict | Set `z-index` on your overlays to > 3000, or lower `--ab-chat-widget-z` |
| "MudBlazor already registered" error | `AddMudServices()` called twice | Ensure single call in `Program.cs` |
| Other library's modals hidden when chat widget is open | MudBlazor popover container z-index | Set `MudPopoverProvider` z-index override or adjust your library's z-index |
