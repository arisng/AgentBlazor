# Rubber-Duck Roadmap Review (Audit Trail)

Created: 2026-08-17
Status: Durable research artifact (R5)
Last updated: 2026-08-17

Devil's-advocate review of the v1 roadmap draft (2026-08-17). Every finding was incorporated into the final roadmap as a hardening decision — traceability preserved here.

## Core findings (audit-trail table)

| # | Finding (verified) | Resolution in final roadmap |
|---|---|---|
| B1 | Anthropic-first provider ordering = priority inversion; OpenAI/Azure/Ollama parked "untouched" | Phases reordered OpenAI/OpenAI-compatible first; **Anthropic last** (Phase 6); "untouched" scope removed |
| C1 | Version slot `0.2.24-internal.1` already taken by Markdig release note (`docs/releases/0.2.24-internal.1.md`) | Phase 0 is docs-only (rides current version or `0.2.25-internal.1`); release table renumbered |
| C2 | "14 committed lock files" claim wrong — `**/packages.lock.json` gitignored, 0 tracked | Corrected in R2 condensation; roadmap uses `--force-evaluate` (CI) + local locks only |
| C3 | "0 errors" asserted unconditionally | Qualified to "probe machine (Windows/Debug/`--no-incremental`); CI re-verification is Phase 1 gate 1" |
| C4 | `AgentTurnResponse.Usage` cited `:24`, actual `:23` | Fixed throughout |
| C5 | "adapter sends only instructions+user message" misimplied history absent | Recast: history IS on the wire via MAF `AgentSession` but **unbounded/un-budgeted**; Phase 4 adds the budget |
| C6 | MAF floors derived from probe NU1605/NU1100, not nuspec (IP-blocked) | Noted in Confidence + R4 |
| C7 | **DeepSeek usage schema absent from design** (biggest gap) | New provider-schema matrix phase (Phase 3) + schema wire fixtures |
| C8 | `Anthropic` SDK package id unpinned → `Anthropic*` needed in NuGet.Config | Added to Phase 6 scope |
| C9 | `responses-api-escape-hatch.md` cited as containing caching content it lacks | File extension moved into Phase 5 (OpenAI explicit cache) |
| C10 | Provider method naming drift (`UseOpenAI` vs `AddOpenAIProvider`) | `UseOpenAI` @ `AgentBlazorRegistrationOptions.cs:32/44`; `AddOpenAIProvider` @ `AgentProviderRegistrationExtensions.cs:39-47` |
| C11 | Tools-ordering determinism asserted as fact (hash-seeded iteration, no cross-restart test) | Phase 4/5 deliverable: cross-process deterministic tool serialization test |
| C12 | Single-OS CI vs "0 errors" | Resolved via C3 qualification; Windows/Linux matrix considered for ProviderAdapters/Hosting in Phase 2 |
| C13 | CI smoke version hardcoded `0.2.0` | Phase 1: parameterize from `Directory.Build.props` |
| D2 | No version-graph assertion in CI | Phase 1 gate adds `dotnet list <proj> package` |
| D3 | No negative cache-contract test | Added to Phase 4/5 gates (mutate middle block ⇒ cached tokens drop to 0) |
| D5 | Cost-ceiling/cache-ratio floor optional & last | **Hard nightly gate** in Phase 3 with per-provider "unassertable" handling |
| D6 | Live smoke has no deterministic CI form | Cassette/recorded-response playback through `HttpListenerWireServer` |
| E1 | Docs skeleton missing ✅s, Recorded Decisions, Out of Scope, Relevant Files, audit chapter | Applied in the final roadmap |
| E2 | Research refs machine-local only | `docs/internal/research/` condensation (Phase 0) |
| E3 | plan.md/STATUS.md update deferred to GA | Moved into Phase 0 |
| E4 | Atomization | Roadmap + research-refs + plan/STATUS commit as separate docs-only atomic commits |

## Source

- Session research directory (rubber-duck outputs, 2026-08-17); final roadmap "Rubber-Duck Review Findings" section
- Durable copy: session index R7; condensed into `docs/internal/research/rubber-duck-roadmap-review.md` (2026-08-17)