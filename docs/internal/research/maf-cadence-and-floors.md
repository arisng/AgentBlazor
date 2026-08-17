# MAF Release Cadence & Dependency Floors

Created: 2026-08-17
Status: Durable research artifact (R4)
Last updated: 2026-08-17

MAF (Microsoft Agent Framework) release rhythm and the floor matrix the roadmap's phase gates must anticipate.

## Core findings

- **GA cadence ≈ 7.3 days per minor** (tags 1.1.0 → 1.17.0); previews tag to the same GA minor; **no LTS / no backports**.
- **Floor jumps to watch (1.14-class block):** MEAI / DI / System.Text.Json / System.ClientModel / Azure.Core climb as a block.
  - 1.14 → DI(.Abstractions)/STJ 10.0.9 + `AGUI.*` package split (restore mapping needed).
  - 1.16/1.17 → MEAI floor 10.7.0 (latest 10.9.0).
- Floor derivation is from probe NU1605/NU1100 evidence, not nuspec inspection (IP-blocked on the probe machine — caveat on confidence; rubber-duck C6).
- Upstream watch list: `microsoft/agent-framework` releases atom + `dotnet/Directory.Packages.props` on main (no floor bumps queued as of 08-16); Anthropic SDK CHANGELOG (caching ≥12.8); openai-dotnet CHANGELOG (cache keys ≥2.12); Azure.AI.OpenAI static 2.9.0-beta.1 floor.

## Source

- MAF tags 1.1.0 → 1.17.0 + `main` `dotnet/Directory.Packages.props`; probe restore logs (2026-08-16/17)
- Durable copy: session index R6; condensed into `docs/internal/research/maf-cadence-and-floors.md` (2026-08-17)