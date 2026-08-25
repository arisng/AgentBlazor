# Features Catalog Schema

The empirical audit of the Demo project produces `audit.json`. Durable knowledge is
maintained in `demo-features-catalog.md` with a status field per feature. This file
documents the catalog shape, the status taxonomy, and how to keep the two in sync.

## Status taxonomy

Every cataloged feature carries exactly one **status**. Status is evidence-gated:
a feature may only be marked `implemented` if the audit (or the code it inspects)
shows it wired in the Demo. Do not mark features `implemented` purely because the
library supports them.

| Status        | Meaning                                                                 |
|---------------|-------------------------------------------------------------------------|
| `implemented` | Wired and demoable in the Demo — audit evidence exists.                 |
| `wired-dormant`| Wired in `Program.cs`/config but currently inactive (e.g. provider/key not set, `UseDevTools` commented out). |
| `partial`     | Implemented for a subset (e.g. one provider, one storage adapter).      |
| `not-demoed`  | Library feature that exists but the Demo has no page/wiring for it yet. |
| `proposed`    | Candidate feature — not yet implemented in the Demo.                    |

## Catalog entry anatomy

Use this shape for each feature row in `demo-features-catalog.md`:

```markdown
## <Feature grouping>

### <Feature Name>
- **Status**: implemented | wired-dormant | partial | not-demoed | proposed
- **What it demonstrates**: <one sentence, user-visible outcome>
- **Where in the Demo**: <file:line or page route / capability action id>
- **Audit evidence**: <what `audit.json` shows for this feature>
- **ab\* skill**: <which .github/skills/ab-\* skill owns the implementation details>
```

## Keeping the catalog honest

1. **Run the audit first** — regenerate fresh evidence before editing the catalog.
   ```powershell
   pwsh .github/skills/ab-demo-feature-overseer/scripts/audit-demo-features.ps1 -OutputPath artifacts/demo-audit.json
   ```
2. **Only mark `implemented` with current evidence.** If audit omits a feature you
   know exists, extend the script (see below) rather than fudging status.
3. **Record the ab\* skill** that owns implementation details for each feature, so a
   maintainer can dig into the how.
4. **Use `audit.json -> agentRegistrations`** as the authoritative per-agent evidence:
   each entry carries `name`, `kind` (agent|workflow), `capabilitiesType`,
   `allowedComponents[]`, `dataSchemas[]`, `hasSharedInstructions`, `routePrefixes`.
   Prefer this over `agents` / `workflows` flat lists when you need the per-agent map.

## Coverage matrix (living doc)

`references/demoable-coverage-matrix.md` is the maintained living doc: it lists the full
spectrum of **demoable** features and their implementation coverage in the Demo, using
the same status taxonomy (✅ implemented / 🟡 wired-dormant / 🔶 partial / ⛔ not-demoed).
It is the place to look for "which demoable features exist but are not yet in the Demo".
Keep it in sync with this catalog and refresh its evidence on every audit.

## Extending the audit script

The script is deliberately probe-based. When a new feature type is demoed:

- Add a section that greps the Demo source for the wiring tokens and emits the
  evidence into the matching `$report.<key>` collection.
- Re-run the script and confirm the new collection populates.
- Then add the feature to the catalog with status backed by that evidence.

Keep regexes anchored to Demo source (`demo/AgentBlazor.Demo`, excluding
`bin`/`obj`) — never scan the library or NuGet packages for Demo "features".