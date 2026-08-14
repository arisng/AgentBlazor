# UAT Bucket — Tools & Middleware

> Aspect: service tool / MCP tool invocation and the agent turn-pipeline middleware (AgentBlazor).
> Status: **SCAFFOLD** — no test cases yet. Populate incrementally as `ab-tool-registration`
> and `ab-middleware-authoring` work lands.
> **Prefix:** `TOOL-###` (bucket-local sequence; scaffolds start at `TOOL-001` as cases land).
> Shared prerequisites: see `../runbook.md` once cases land (boot, personas, auth, evidence).

## Intended scope

Cases here assert that tools and middleware behave correctly end-to-end:

- **Tool registration** (`ab-tool-registration`): `AddTool` service tools, MCP servers
  (`UseMcpServer` / `HttpMcpToolProvider`), tool parameter binding (`AgentToolParameter`),
  DI access in handler delegates, per-agent action filters (`WithAllowedActions`).
- **Middleware** (`ab-middleware-authoring`): `IAgentTurnMiddleware`, `AgentTurnContext`,
  cross-cutting concerns (logging, cost control, tenant enrichment, audit, rate limiting),
  short-circuiting, registration order via `UseMiddleware`.

## Example case shapes (draft — populate when the feature is exercised)

- Tool invocation: an agent action that surfaces a registered service tool results in the
  tool handler being called (assert via handler side-effect/log) and the output rendered.
- MCP wiring: an MCP server's tools are discoverable and invocable through the agent.
- Middleware ordering: a short-circuiting middleware prevents downstream pipeline stages;
  a tenant-enrichment middleware populates `ITenantContext` before capability execution.
- Cost control: a middleware that caps/halts per-turn spend enforces its threshold.

> Placeholder — replace with concrete GIVEN/WHEN/THEN cases as feature work lands.
