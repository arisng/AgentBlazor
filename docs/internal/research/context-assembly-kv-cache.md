# Context Assembly & KV-Cache Design (AgentBlazor Internals)

Created: 2026-08-16
Status: Durable research artifact (R1)
Last updated: 2026-08-17

Condensed from the session research artifact "how-to-design-context-assembly-that-comply-to-the-..." for repo-relative durability.

## Core findings

- **Wire order is already cache-friendly today:** static system instructions → append-only history → dynamic tail (`Runtime context:` block sorted, last). The single-turn adapter assembly is not the problem; the absence of budget/breakpoint control is.
- **`MaxHistoryInPrompt` is dead config:** declared in `src/AgentBlazor.Core/Options/ConversationOptions.cs:25`; `RuntimeConversationHistory.ToExecutionTurns` has tests but **no runtime consumer**. History is injected by MAF `AgentSession` — it IS on the wire, but **unbounded/un-budgeted** (rubber-duck C5 correction).
- **No cache-breakpoint control:** nothing can mark system/tool/history blocks with provider cache directives; middleware cannot modify the system prompt or tools (adapter boundary).
- **Usage stats stripped:** `ExtractUsage` (`ChatClientRuntimeAdapter.cs:2199-2210`) aggregates `UsageContent` → `AgentTurnResponse.Usage` (`AgentTurnResponse.cs:23`); demo JSONL logs no cache fields and prices input at full rate; `CachedInputTokenCount` has zero assertion surface.
- **`BuildUserMessage` tail ordering** (`ChatClientRuntimeAdapter.cs:3028-3085`): the dynamic tail must stay last and byte-stable for the stable-prefix invariant.
- **Invariant:** byte-identical stable prefix ⇒ cached tokens > 0 (positive); mutating a middle block ⇒ cached tokens drop to 0 (negative).

## Source

- Repo: arisng/AgentBlazor @ `7f3a78a…` (2026-08-16), branch `develop`
- Durable copy: session index R1; condensed into `docs/internal/research/context-assembly-kv-cache.md` (2026-08-17)