# MAF 1.17.0 Upgrade Probe (Empirical)

Created: 2026-08-16
Status: Durable research artifact (R2)
Last updated: 2026-08-17

Condensed from the session research artifact "maf-upgrade-empirical-and-kv-cache-context-assembly-...".

## Core findings

- **Full solution compiles at 0 errors on MAF 1.17.0** — probe machine only (Windows/Debug/`--no-incremental`); CI re-verification remains a Phase 1 gate (rubber-duck C3/C12).
- Only source-level change is the **AG-UI rename** (2 files): `AddAGUI()` → `AddAGUIServer()` (`AgentBlazorHostingServiceCollectionExtensions.cs:11`); `MapAGUI(pattern, agent)` → `MapAGUIServer(pattern, agent)` (`AgentBlazorAgUiEndpointRouteBuilderExtensions.cs:20`).
- Restore blockers cleared by: `Microsoft.Extensions.DependencyInjection(.Abstractions)` 10.0.9 (NU1605) and `<package pattern="AGUI.*" />` in `NuGet.Config` (NU1100 — AGUI.Abstractions/Server 0.0.3 transitive).
- Resolved graph at probe: MAF 1.17.0 / MEAI 10.7.0 / OpenAI 2.10.0 / AGUI 0.0.3.
- **MAF ships zero caching code** — options merge/`RawRepresentationFactory` pass-through only; AgentBlazor must add its own normalization shims/decorators.
- Corrected claim (C2): `**/packages.lock.json` is gitignored — **0 tracked dotnet locks**, not 14; CI restore uses `--force-evaluate`.

## Source

- Repo: arisng/AgentBlazor @ `7f3a78a…`; MAF tag `dotnet-1.17.0` @ `1da5718…` (2026-08-16)
- Durable copy: session index R2; condensed into `docs/internal/research/maf-upgrade-probe.md` (2026-08-17)