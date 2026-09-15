---
name: ab-licensing
description: "AgentBlazor licensing tiers (Free/Paid/Premium), entitlement gating, license key activation, and per-tier service mapping. Use when answering questions about what features require a paid license, how to activate a license key, what services change between tiers, or how the entitlement system works. Triggers: license, tier, pricing, paid features, enterprise features, AB-PRO, AB-ENT, entitlement, UseProLicense, AddAgentBlazorLicensing."
metadata:
    version: 0.1.0
---

# AgentBlazor Licensing

## Tier Model

| Market Name | C# Enum | Key Prefix | Price | Target |
|-------------|---------|------------|-------|--------|
| **Free** | `AgentBlazorTier.Free` (0) | _(none)_ | $0 | Solo devs, POCs |
| **Pro** | `AgentBlazorTier.Paid` (1) | `AB-PRO-` | $29/seat/mo | Teams |
| **Enterprise** | `AgentBlazorTier.Premium` (2) | `AB-ENT-` | Custom | Large orgs |

**Important:** The C# enum uses `Paid`/`Premium`, not `Pro`/`Enterprise`. Enterprise keys (`AB-ENT-*`) map to the `Premium` enum value.

## Two Independent Tier-Setting Paths

There are **two separate DI mechanisms** for setting the tier. They are parallel, not chained.

### Path 1: `UseProLicense()` — Options + Service Swaps

Activates paid/enterprise features via a license key. Swaps DI services from null/static defaults to durable SQLite + LLM implementations.

```csharp
builder.Services.AddAgentBlazor(options =>
{
    // Pro tier (AB-PRO- prefix)
    options.UseProLicense("AB-PRO-VALID-KEY-12345678");

    // Enterprise tier (AB-ENT- prefix) → maps to Premium enum
    options.UseProLicense("AB-ENT-VALID-KEY-12345678");

    // Optional: custom data directory for SQLite persistence
    options.UseProLicense("AB-PRO-...", "/data/agentblazor");
});
```

Sets `AgentBlazorOptions.LicensedTier` and registers paid service implementations.

### Path 2: `AddAgentBlazorLicensing()` — Entitlement Service

Registers `IAgentBlazorEntitlementService` singleton for runtime entitlement checks.

```csharp
builder.Services.AddAgentBlazorLicensing(AgentBlazorTier.Paid);
```

### Tier Resolution Fallback

At runtime, the effective tier is resolved as:

```
_entitlementService?.CurrentTier ?? _options.Value.LicensedTier
```

If `IAgentBlazorEntitlementService` is not registered (null), it falls back to `AgentBlazorOptions.LicensedTier`. This two-layer pattern means both paths should be configured consistently to avoid silent mismatches.

## License Key Validation

- Empty/whitespace key → `ArgumentException`
- Wrong prefix (not `AB-PRO-` or `AB-ENT-`) → `ArgumentException` with prefix hint
- Key shorter than 24 characters → `ArgumentException`
- Data directory pointing to a file → `InvalidOperationException` ("points to a file")
- Non-writable data directory → `InvalidOperationException` ("not writable")
- Missing data directory → auto-created; path normalized via `Path.GetFullPath`
- Default data directory: `Environment.CurrentDirectory`

## DevTools (License-Free)

Dev tools are separate from paid licensing — available on Free tier:

```csharp
options.UseDevTools(autoShow: true);
```

Replaces `IAgentInspectorStore` with **`InMemoryAgentInspectorStore`** (not Null, not Sqlite). Inspector data is not persisted across restarts.

## Service Mapping by Tier

### Free Tier Defaults (no license)

| Interface | Implementation | Behavior |
|-----------|---------------|----------|
| `IActionHistoryStore` | `NullActionHistoryStore` | No-op, no persistence |
| `IAdaptiveSuggestionService` | `StaticSuggestionService` | Static suggestions only |
| `IProactiveInsightService` | `NullProactiveInsightService` | No-op |
| `IAgentInspectorStore` | `NullAgentInspectorStore` | No-op |

### Paid/Enterprise Tier (via `UseProLicense`)

Both `AB-PRO-*` and `AB-ENT-*` register **identical services** — they are functionally the same today.

| Interface | Implementation | Behavior |
|-----------|---------------|----------|
| `IActionHistoryStore` | `SqliteActionHistoryStore` | Durable history with user/session indexing |
| `IAdaptiveSuggestionService` | `LlmAdaptiveSuggestionService` | LLM-powered adaptive suggestions |
| `IProactiveInsightService` | `LlmProactiveInsightService` | LLM-powered proactive insights |
| `IAgentInspectorStore` | `SqliteAgentInspectorStore` | Durable inspector runs |
| `IUsageAnalyticsService` | `SqliteUsageAnalyticsService` | Summary, trends, anomalies |
| `IAuditLogService` | `SqliteAuditLogService` | Compliance audit trail (CSV/JSON export) |
| `ISmartSuggestionService` | `SqliteSmartSuggestionService` | Pattern-based suggestions with sequence analysis |

### SQLite Database Layout

| Database File | Services Using It |
|--------------|-------------------|
| `agentblazor-history.db` | `IActionHistoryStore`, `IUsageAnalyticsService`, `ISmartSuggestionService` |
| `agentblazor-inspector.db` | `IAgentInspectorStore` |
| `agentblazor-audit.db` | `IAuditLogService` |

## Component Action Tier Gating

**All currently shipped built-in component actions are Free tier.** The design philosophy: core component interaction is part of the free platform; paid value comes from intelligence, history, and insights.

`AgentComponentTierBoundaries.GetRequiredTier()` returns `AgentBlazorTier.Free` for any unrecognized `componentId:actionId` — unknown actions are never blocked by tier.

For the full action-to-feature mapping, see [references/tier-features.md](references/tier-features.md).

## Runtime Tier Filtering

Tier enforcement is checked in two runtime paths:

1. **Capability policy** — `RuntimeCapabilityPolicy.Evaluate()` applies agent-level allowlists then tier gating, returning `AllowedCapabilities`, `BlockedByAgentPolicy`, `BlockedByTier`
2. **Plan validation** — `RuntimePlanResponses.BuildValidationFailureResults()` validates execution plans against the effective tier

Both paths use `AgentComponentTierBoundaries.GetRequiredTier()`.

## Enterprise vs Pro: Current State

| Aspect | Pro (`AB-PRO-`) | Enterprise (`AB-ENT-`) |
|--------|-----------------|----------------------|
| Enum value | `AgentBlazorTier.Paid` | `AgentBlazorTier.Premium` |
| `IsEnabled(Paid)` | true | true |
| `IsEnabled(Premium)` | **false** | true |
| Service wiring | 7 paid services | **Same 7 paid services** |
| Future gating | Standard features | SSO, SLA, dedicated support |

Both tiers register identical service implementations today. Premium is reserved for future enterprise-specific features.

## Telemetry

`DeterministicAgUiHostedAgent` reports `_entitlementService?.CurrentTier.ToString()` in `AgentBlazorRunTelemetryEvent.Tier`.

## Key Files

| File | Purpose |
|------|---------|
| `src/AgentBlazor.Licensing/AgentBlazorTier.cs` | Tier enum: Free(0), Paid(1), Premium(2) |
| `src/AgentBlazor.Licensing/IAgentBlazorEntitlementService.cs` | Entitlement check interface |
| `src/AgentBlazor.Licensing/AgentBlazorEntitlementService.cs` | Entitlement: `IsEnabled(requiredTier) => CurrentTier >= requiredTier` |
| `src/AgentBlazor.Licensing/LicensingServiceCollectionExtensions.cs` | `AddAgentBlazorLicensing(tier)` extension |
| `src/AgentBlazor.Hosting/AgentBlazorRegistrationOptions.cs` | `UseProLicense()` and `UseDevTools()` |
| `src/AgentBlazor.Core/Components/AgentComponentTierBoundaries.cs` | Component action tier map |
| `src/AgentBlazor.Core/Components/ComponentActionPolicy.cs` | Policy evaluation + action key utils |
| `src/AgentBlazor.Core/Runtime/RuntimeCapabilityPolicy.cs` | Runtime capability filtering |
| `src/AgentBlazor.Core/Runtime/ExecutionPlans/RuntimePlanResponses.cs` | Plan-level tier validation |
| `docs/internal/pricing-tiers.md` | Pricing strategy and tier model docs |
