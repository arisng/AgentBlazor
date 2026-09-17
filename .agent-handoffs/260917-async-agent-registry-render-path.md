# Handoff: Async `IAgentRegistry` — remove the sync-over-async block on the Blazor render path

**Date:** 2026-09-17
**Repo:** `c:\Workplace\DProcess\AgentBlazor` (branch `develop`, `src/` tree clean)
**Priority:** P1 — the sync `IAgentRegistry` contract makes a *correct* consumer implementation deadlock the Blazor Server renderer. A consumer hit this in production-path QA.
**Consumer:** `dprocess-dotnet-starter-kit` → `src/Playground/Playground.Lifeline` (pins `AgentBlazor` `0.2.25-internal.4`)

## What this session should focus on

Add an **async twin of `IAgentRegistry`** to the AgentBlazor packages, adopt it in
`AgentChatSurface` so no registry call ever blocks the renderer, and publish a new
private-feed version the consumer can pin.

The goal is **not** to make `IAgentRegistry` async (that is a breaking change). The goal
is to give the library an async read path that a data-backed registry can implement, so
`AgentChatSurface` never has to block.

## Why: the consumer deadlock (read this before designing)

A consumer registry hydrated agent definitions over HTTP inside `IAgentRegistry.GetAll()`
(the only seam the interface offers). Because `GetAll()` is called from
`AgentChatSurface.OnInitialized()`, the consumer had to write
`service.ListAsync().GetAwaiter().GetResult()`.

Blazor Server's `RendererSynchronizationContext` is **single-threaded**. Blocking inside a
lifecycle hook on an operation whose continuation is queued back to that same context is a
guaranteed deadlock: the blocked thread can never pump the continuation. It does not fail
fast — it wedges the **entire server**, because the blocked thread holds the renderer.

**Full diagnosis, dump evidence, and the consumer-side stopgap are already written up —
do not re-derive them:** consumer repo → `.agent-handoffs/260917-lifeline-login-ssr-deadlock-investigation.md`
and `.agent-handoffs/260917-agentchat-entity-rebuild-and-ssr-deadlock.md`.

The consumer shipped a stopgap (`ConfigureAwait(false)` on the awaiting chain). That works
but is **fragile by construction**: it only holds while *every* `await` beneath every
`GetAll()` call site remembers to opt out of context capture. The next edit to any handler
on that chain silently re-arms the deadlock. This handoff exists to replace that
convention-dependent safety with a structural one.

## Current surface (verified in this tree)

`src/AgentBlazor.Core/Agents/IAgentRegistry.cs` — three synchronous methods, no async:

```csharp
namespace AgentBlazor.Agents;

public interface IAgentRegistry
{
    IReadOnlyCollection<AgentRegistration> GetAll();

    bool TryGet(string name, out AgentRegistration registration);

    void AddOrUpdate(AgentRegistration registration);
}
```

Implementors (the two that must keep compiling):

| Implementor | Location | Note |
|---|---|---|
| `InMemoryAgentRegistry` | `src/AgentBlazor.Core/Agents/InMemoryAgentRegistry.cs` | Library default, pure in-memory |
| `DatabaseBackedAgentRegistry` | `demo/AgentBlazor.Demo/Services/DatabaseBackedAgentRegistry.cs` | **Demo has the same latent bug** — see below |

DI registration: `src/AgentBlazor.Core/Services/AgentBlazorServiceCollectionExtensions.cs:54`
(`TryAddSingleton<IAgentRegistry>(sp => BuildAgentRegistry(...))`). The demo documents the
"replace" path (`AddSingleton<IAgentRegistry>(...)` **before** `AddAgentBlazor`) in the
`DatabaseBackedAgentRegistry` remarks at lines 17–24 — the consumer uses the same pattern.

### The demo ships the same bug (fix it in this change)

`demo/AgentBlazor.Demo/Services/DatabaseBackedAgentRegistry.cs` calls `EnsureLoaded()`
from `GetAll()` (line 50), `TryGet()` (line 56) and `AddOrUpdate()` (line 63), and
`EnsureLoaded()` runs a **synchronous EF Core query**. Reached from
`AgentChatSurface.OnInitialized()`, that is the identical sync-over-async-on-the-renderer
pattern the consumer hit — the demo is simply spared it today because its SQLite/local
query usually completes without posting back to the renderer context. Adopt the async twin
here too, or this repo ships a known-bad reference implementation.

## Exact call sites to change (all in one file)

`src/AgentBlazor.Components/Chat/AgentChatSurface.razor` — verified call graph:

| Line | What | Change |
|---|---|---|
| `19` | `@inject IAgentRegistry AgentRegistry` | inject `IAsyncAgentRegistry` instead |
| `654–665` | `protected override void OnInitialized()` → `AgentRegistry.GetAll()` at **661** | convert to `OnInitializedAsync()`; `await GetAllAsync()` |
| `667–672` | `protected override async Task OnParametersSetAsync()` | already async — awaits the renamed policy method |
| `1876` | `private void ApplyAgentSelectionPolicy()` | rename to `...Async()`, `await` the registration read |
| `1894` | `var resolved = ResolveRouteLockedAgentName();` | becomes `await ResolveRouteLockedAgentNameAsync()` |
| `1916–1950` | `private string? ResolveRouteLockedAgentName()` → `AgentRegistry.GetAll()` at **1927** | rename to `...Async()`; `await GetAllAsync()` |
| `479` | `private readonly List<string> _agentNames = [];` | unchanged — no cache field required |

Notes that matter for the implementation:

- `OnInitializedAsync()` is the idiomatic Blazor swap and is available — the component
  already overrides `OnParametersSetAsync`, so async lifecycle use is established here.
- `ApplyAgentSelectionPolicy()` is called **twice**: at `664` (inside `OnInitialized`) and
  at `669` (inside `OnParametersSetAsync`). Both become `await`-ed calls.
- **Both `GetAll()` call sites can be de-blocked by making the chain async** — see the
  verified call graph below; no field cache is needed, which also avoids the cache
  invalidation problem entirely.
- `ChatClientRuntimeAdapter` (`src/AgentBlazor.Core/Runtime/Adapters/ChatClientRuntimeAdapter.cs:41`)
  also depends on `IAgentRegistry`, but only at **turn-execution** time, not on the render
  path. No change required there.

### The route-locked selection path — no cache needed (verified)

The second `GetAll()` call site looked like it forced a cache field because
`ResolveRouteLockedAgentName()` is synchronous. It does not. Verified call graph — both
methods are `private` to `AgentChatSurface.razor` and have exactly these callers:

```
ApplyAgentSelectionPolicy()          declared :1876    called :664 (OnInitialized), :669 (OnParametersSetAsync)
ResolveRouteLockedAgentName()        declared :1916    called :1894 (inside ApplyAgentSelectionPolicy)
```

Both call sites are convertible, so **make the whole chain async and skip the cache**:

- `:664` is inside `OnInitialized()` — converting to `OnInitializedAsync()` makes it awaitable.
- `:669` is already inside `OnParametersSetAsync()`.

So: `ApplyAgentSelectionPolicy()` → `ApplyAgentSelectionPolicyAsync()` and
`ResolveRouteLockedAgentName()` → `ResolveRouteLockedAgentNameAsync()`. This removes
**both** blocking calls with **no cache field and no staleness question**, and the surface
keeps its current fresh-read semantics. **Prefer this over caching.**

The cache-field approach (store registrations in a field during load, read memory in the
sync method) is the fallback if some constraint you find makes the async rename awkward —
but then you inherit an invalidation problem: the surface re-reads on every call today, so
a mid-session `AddOrUpdate` is picked up now and would not be with a cache. Only take that
path if you also solve invalidation.

## Recommended design: additive async twin, non-breaking

There is a **direct precedent in this repo, shipped one commit ago** — follow it rather
than inventing a new pattern:

- `src/AgentBlazor.Core/Runtime/Interfaces/IConversationStore.cs:123` adds
  `GetSessionSummariesAsync(...)` as a **default interface method**, so existing custom
  stores keep compiling untouched.
- Documented in `docs/releases/0.2.26-internal.1.md` under *"Default interface method"* —
  explicit **"zero breaking changes for existing custom stores."**

Apply the same shape. **Recommended form (option 2 below)** — the async twin extends the
sync interface so there is exactly one awaitable path and no capability-check branches in
the component:

```csharp
namespace AgentBlazor.Agents;

/// <summary>
/// Async companion to <see cref="IAgentRegistry"/> for registries that hydrate agent
/// definitions from an I/O-bound source (HTTP, database).
/// </summary>
/// <remarks>
/// <para>
/// The Blazor renderer's SynchronizationContext is single-threaded. Blocking a lifecycle
/// hook on an operation whose continuation is queued back to that same context deadlocks
/// the entire server — it does not fail fast. Implement a real override of
/// <see cref="GetAllAsync"/> whenever the underlying read is I/O-bound, rather than letting
/// the default delegate to the blocking <see cref="IAgentRegistry.GetAll"/>.
/// </para>
/// </remarks>
public interface IAsyncAgentRegistry : IAgentRegistry
{
    /// <summary>
    /// Reads all agent registrations without blocking. The default implementation delegates
    /// to <see cref="IAgentRegistry.GetAll"/> and is only safe for in-memory registries.
    /// </summary>
    Task<IReadOnlyCollection<AgentRegistration>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetAll());

    async Task<AgentRegistration?> TryGetAsync(string name, CancellationToken cancellationToken = default)
        => (await GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    Task AddOrUpdateAsync(AgentRegistration registration, CancellationToken cancellationToken = default)
    {
        AddOrUpdate(registration);
        return Task.CompletedTask;
    }
}
```

This mirrors `IConversationStore.GetSessionSummariesAsync` exactly: a default member body
means existing implementors are **source-compatible with zero edits**, which the
`0.2.26-internal.1` release notes call out as *"zero breaking changes."* Say the same thing
in your release notes.

Alternative (option 1, **not** recommended): keep `IAsyncAgentRegistry` standalone and
capability-check in the component
(`AgentRegistry is IAsyncAgentRegistry r ? await r.GetAllAsync() : AgentRegistry.GetAll()`).
More flexible for third-party registries, but it leaves a **blocking fallback branch** in
the render path — the exact hazard this handoff exists to remove — and requires the sync
path to be documented as blocking forever.

### Two DI traps this design must handle

Because `AgentChatSurface` will inject `IAsyncAgentRegistry`, DI must be able to resolve it
even when an app registered only `IAgentRegistry`:

1. **Register the async interface in `AddAgentBlazorServices`.** Alongside the existing
   `TryAddSingleton<IAgentRegistry>(...)` (`AgentBlazorServiceCollectionExtensions.cs:54`),
   add a `TryAddSingleton<IAsyncAgentRegistry>` resolution. A naive
   `(IAsyncAgentRegistry)sp.GetRequiredService<IAgentRegistry>()` cast will throw for any
   registry that implements only the sync interface — precisely the pre-existing
   third-party registries this design must not break.
2. **Therefore declare it explicitly on the built-in registry.** Change
   `InMemoryAgentRegistry` to `: IAsyncAgentRegistry`. That is a **one-word source change**,
   not "no source change" (an earlier draft of this handoff overstated it): the *interface*
   change is non-breaking for outsiders, but the *library's own* default registry must be
   annotated so the async resolution is safe. When *both* interfaces resolve, make sure they
   resolve to the **same singleton instance** — two instances means the surface renders a
   stale agent list.

## Consumer-side follow-up (separate session, `dprocess-dotnet-starter-kit`)

Do not change these here — they are the consumer's half, listed so the interface lands
compatible on the first try.

| File | Line(s) | What |
|---|---|---|
| `src/Directory.Packages.props` | `12` | bump `AgentBlazor` version pin |
| `src/Playground/Playground.Lifeline/Program.cs` | `30`, `341–350` | alias + `TryAddSingleton<IAgentRegistry>` mapping; also register the map for `IAsyncAgentRegistry` **to the same singleton instance** |
| `src/Playground/Playground.Lifeline/Services/AgentChat/AgentDefinitionBffRegistry.cs` | `52–55`, `78`, `91`, `115`, `196`, `226–234` | implement the async twin; `Hydrate` (`:234`, `ListAsync().GetAwaiter().GetResult()`) becomes `HydrateAsync`; blocking also at `:123`, `:127`, `:156` |
| `src/Playground/Playground.Lifeline/Services/Api/AgentDefinitionBffService.cs` | 6 awaits | `ListAsync` etc. — the `ConfigureAwait(false)` stopgap can then be reverted |
| `src/Playground/Playground.Lifeline/Services/Api/LifelineAuthorizationHandler.cs` | 8 awaits | same; stopgap can be reverted once nothing blocks |
| `src/Tests/Integration.Tests/Playground/Lifeline/LifelineSsrSyncOverAsyncDeadlockTests.cs` | — | existing regression guard; re-run after the pin bump |

**Consuming the new package:** `NuGet.Config` in the consumer already maps `AgentBlazor*`
to the `local-agentblazor` source (`%USERPROFILE%\.agentblazor-feed`). No source change
needed; only the version pin.

**DI gotcha (already documented, still applies):** `AgentDefinitionBffService` takes the
**concrete** registry type (it calls `Refresh()` after writes), so the concrete type and
*both* interfaces must resolve to the **same singleton instance**. Registering only the
interface mapping causes a runtime DI crash that build/test/arch suites do **not** catch —
only host startup does. See consumer `.agent-handoffs/260917-agentchat-entity-rebuild-and-ssr-deadlock.md` §1.

## Tests to add (all runnable in-repo — preferred over consumer-side proof)

`tests/AgentBlazor.Components.Tests` is a **bUnit** suite. `AgentChatSurfaceTests.cs` is a
`TestContext` subclass with 10 test methods and 12 `RenderComponent<AgentChatSurface>` sites.
Establishing harness, verbatim from the file:

```csharp
public sealed class AgentChatSurfaceTests : TestContext
{
    [Fact]
    public void StopButton_CancelsActiveStreamingRun_AndRendersCanceledOutcome()
    {
        Services.AddAgentBlazorServices();
        Services.AgentBlazor().AddAgent("Test Agent");
        Services.AddSingleton<IAgentActionRenderRegistry, TestActionRenderRegistry>();

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, false)
            .Add(static surface => surface.DefaultAgentName, "Test Agent"));
        ...
        cut.WaitForAssertion(() => { ... });
```

So the seam exists and is proven. Note `Services.AddAgentBlazorServices()` registers the
real `IAgentRegistry` via `TryAddSingleton` — to substitute a double, register it
**before** that call, or use `Services.AddSingleton<IAgentRegistry>(...)` and rely on
`TryAdd` semantics. `WaitForAssertion` is the established pattern for awaiting async
component work in this suite.

Add:

1. **Async path is used** — register an async registry double; render `AgentChatSurface`;
   assert the surface awaited the async read and **never** called blocking `GetAll()`.
2. **No blocking on the render path** — the strongest form: a double whose `GetAllAsync()`
   returns an **incomplete** `Task`, then assert the component renders and the read
   completes on the awaited path. This is the test that would have caught the consumer
   deadlock.
3. **In-memory default still works** — `InMemoryAgentRegistry` through the async interface
   (guards the `Task.FromResult` default inherited from `IAsyncAgentRegistry`).
4. **Route-locked selection still resolves** — after `ApplyAgentSelectionPolicyAsync` /
   `ResolveRouteLockedAgentNameAsync` are awaited, assert the route-locked agent is still
   selected. This is the real regression risk of the rename refactor, and the existing
   suite's `WaitForAssertion` style covers it.

Commands (repo convention; `task` targets already exist):

```powershell
task test:components     # bUnit — the primary gate for this change
task test:core
task test:integration
task test                # all, Release, --no-build after task test:build
```

## Packaging and release steps

1. Bump `<Version>` in `Directory.Build.props` (currently `0.2.26-internal.1`).
2. Add `docs/releases/<new-version>.md`. **`docs/releases/` is tracked** — verified via
   `git ls-files` (the older handoff `260818-consumer-local-nuget-feed.md` describes it as
   gitignored; that is no longer accurate, no `git add -f` needed).
3. Pack + publish to the private feed:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\publish-private-feed.ps1 -Pack -Version <new-version>
   ```
   Idempotent; `-DryRun -Verbose` previews. Feed root is
   `%USERPROFILE%\.agentblazor-feed` (flat mirror of the **current** version). Every
   version also gets a per-version `<feed>\<version>\` folder (`0.2.23-internal.1` through
   `0.2.26-internal.1`, all confirmed present) that must be added as a separate source to
   pin exactly — see `260818-consumer-local-nuget-feed.md` for the layout rationale. A new
   version folder appears automatically when you run the publish script with `-Pack`.
4. Tag `v`-prefixed to match the existing 21 tags (`v0.2.24-internal.2` is the newest).
5. Publish/consume docs: `docs/internal/private-feed-publishing.md` is the canonical
   consumer guide — reference it, do not re-author it.

## Open decisions for this session

1. **Version to ship on.** `0.2.26-internal.1` is **already packed into the feed** and
   contains the unrelated `GetSessionSummariesAsync` work (`docs/releases/0.2.26-internal.1.md`).
   Decide: ship this change on top of `0.2.26-internal.1` (re-pack the same version — the
   consumer would get both changes at once), or bump to `0.2.26-internal.2` for a clean
   delta. **Recommendation: bump to `0.2.26-internal.2`.** Rationale: `0.2.26-internal.1`
   is already packed into the feed (root + its own version folder), and re-packing a
   published version number is bad hygiene — consumers that already restored it would
   silently get different bits, and the consumer would receive this change bundled with the
   unrelated `GetSessionSummariesAsync` work instead of as a reviewable delta.
   (Secondary, weaker signal: `Directory.Packages.props` is among the dirty files, so the
   `0.2.26-internal.1` artifact may have been built with dependency versions that differ
   from the current clean-`src` state. All packaged code lives under `src/`, which is
   clean, so this is a possible-enough concern to avoid, not a proven defect.)
2. **Interface shape** — option 1 (standalone + capability check) vs option 2
   (`IAsyncAgentRegistry : IAgentRegistry`, recommended). See above.
3. **Scope of the async twin** — `GetAllAsync` only, or also `TryGetAsync` /
   `AddOrUpdateAsync`? The render path needs only the read; the consumer's write paths
   (`AgentDefinitionBffRegistry.AddOrUpdate`) also block but are off the render path.
   **Recommendation: include all three** as default methods (cheap, symmetric, closes the
   consumer's remaining blocking sites in one hop).
4. **~~`ResolveRouteLockedAgentName` refactor~~ — resolved.** Verified during this handoff:
   both methods are `private` with exactly two callers, both convertible to async, so the
   whole selection chain can be awaited with **no cache field** and no staleness risk. Do it
   that way; caching is a fallback only. See "The route-locked selection path" above.

5. **Does anything outside this file call the selection methods?** Verified **no** —
   repo-wide search across `src/`, `tests/`, and `demo/` found references only inside
   `AgentChatSurface.razor`. So the `...Async()` renames are safe. Re-verify after your edit
   in case that changes.

## Working-tree state (do not lose)

Uncommitted work exists across **35+ entries**, and it is **not confined to `demo/`** —
correcting an earlier misread of this tree:

| Area | State | Bearing on this work |
|---|---|---|
| `src/` | **clean — zero changed paths** | Package code starts from a known-good base |
| `demo/` | 20 `M`, 11 `D`, 3 `??` — EF Core migration deletion, DbContext/DbContextFactory, configuration options, `AgentBuilder.razor` | Not packaged, **but in the solution** — see below |
| `tests/AgentBlazor.IntegrationTests/` | 2 `M` — `.csproj` **and** `DemoWorkflowDatabaseSeederIntegrationTests.cs` | Directly affects the test gates you are asked to run |
| root | `AgentBlazor.slnx`, `Directory.Packages.props`, `NuGet.Config`, `Taskfile.yml`, `global.json` | `Directory.Packages.props` + `global.json` affect restore/pack |

Three consequences that change how you should work:

1. **The demo is a solution member.** `AgentBlazor.slnx` includes
   `demo/AgentBlazor.Demo.AppHost` (untracked), `demo/AgentBlazor.Demo.ServiceDefaults`
   (untracked), and `demo/AgentBlazor.Demo`. `task test` and `task test:build` run against
   `{{.SOLUTION}}` (`AgentBlazor.slnx`), so **the dirty demo is in the build graph** and a
   demo build break will fail your gates for reasons unrelated to your change.
2. **`tests/AgentBlazor.IntegrationTests` is modified.** Establish a green baseline for it
   *before* your change, or you will not be able to tell your breakage from existing drift.
3. **Prefer `task test:components` / `task test:core`** for the inner loop — project-scoped,
   no demo dependency. Run the full `task test` only once, as the final gate, expecting the
   pre-existing drift above.

**Do not revert or "clean up" this work** — it is someone's in-progress branch state, only
`0` commits ahead of `origin/develop`. If it blocks you, ask before touching it.

Version on disk: `0.2.26-internal.1`. `src/` clean ⇒ the artifact you pack from `src/` is
trustworthy regardless of the `demo/` noise.

## Related handoffs (read before designing — do not duplicate)

| Path | Why |
|---|---|
| `260818-consumer-local-nuget-feed.md` | Canonical feed layout + consume instructions |
| `260910-agent-registration-surface-gaps.md` | Separate `IAgentRegistry`-**adjacent** concerns (registration precedence, tool-surface allow-all fallbacks). Different layer — registration *content*, not *access*. Note its decompile-based findings are confirmed against `0.2.24-internal.3`. |
| `260909-tier2-runtime-adapter-feasibility.md` | Per-turn dynamic layer; complements the static layer |
| consumer `.agent-handoffs/260917-lifeline-login-ssr-deadlock-investigation.md` | Full dump-based diagnosis of the deadlock this handoff fixes structurally |
| consumer `.agent-handoffs/260917-agentchat-entity-rebuild-and-ssr-deadlock.md` | Consumer BFF proxy pattern + the DI same-instance gotcha |

## Suggested skills for the next session

| Skill | Why |
|---|---|
| `ab-capability-authoring` / `ab-entity-design` | Only if the registry change ripples into agent definition shape |
| `ab-conversation-store` | Directly relevant — `GetSessionSummariesAsync` is the precedent pattern to mirror |
| `git-atomic-commit` | Conventional commits; keep the contract change, surface change, tests, version bump, and release notes as separate atomic commits |
| `tdd` | The bUnit seam (`AgentChatSurfaceTests`) supports red-green directly: write the "never blocks the render path" test red first |
| `mssql-cli` | Not needed unless the demo's EF path is exercised against SQL Server |
