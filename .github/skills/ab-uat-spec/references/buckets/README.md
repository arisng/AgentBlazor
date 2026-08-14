# UAT Buckets — AgentBlazor / AgentChat Aspect Index

Each focused bucket is scoped to one aspect within the AgentBlazor library + AgentChat
module. Buckets are **filled incrementally** — a bucket that currently has cases is a
populated bucket; a bucket that only defines its intended scope is a scaffold to be
populated as related features are worked.

Shared prerequisites (boot, ports, tenants × users, auth flows, endpoint inventory,
wire contracts, DB access, evidence conventions, execution order, command templates)
live in the single shared runbook: `../runbook.md`. **Do not duplicate** shared content
in a bucket.

| Bucket file              | Aspect                                                 | Prefix  | Cases now     | ab-* remediation skill                             | Status    |
| ------------------------ | ------------------------------------------------------ | ------- | ------------- | -------------------------------------------------- | --------- |
| `environment`            | Stack boot, ports, tenants×users, auth pre-flight      | `ENV-`  | ENV-001..005  | —                                                  | Populated |
| `auth-permissions`       | 401/403 matrix, role gates, Basic/Admin/root           | `AUTH-` | AUTH-001..006 | —                                                  | Populated |
| `chat-composer-ux`       | Chat surfaces, composer, widget send/reply             | `COMP-` | COMP-001      | `ab-chat-composer`, `ab-mud-components`            | Populated |
| `session-management`     | Browse/resume/hydrate conversations, browser list      | `SESS-` | SESS-001..007 | `ab-chat-session-management`                       | Populated |
| `conversation-store`     | Persistence, fresh-scope rewrite, ResourceId integrity | `PERS-` | PERS-001..004 | `ab-conversation-store`, `ab-entity-design`        | Populated |
| `scoping-registry`       | ResourceType registry, caller scoping                  | `SCOP-` | SCOP-001..007 | —                                                  | Populated |
| `usage-pipeline`         | Usage records, idempotency, cost                       | `USG-`  | USG-001..006  | —                                                  | Populated |
| `bff-proxy`              | BFF proxy store wiring, no-ops, handoff                | `PROX-` | PROX-001..006 | —                                                  | Populated |
| `multitenancy-isolation` | Tenant isolation, root, cross-tenant                   | `MTEN-` | MTEN-001..006 | `ab-multitenancy`                                  | Populated |
| `responsive-viewport`    | Viewport matrix                                        | `VPRT-` | VPRT-001..003 | —                                                  | Populated |
| `agent-capabilities`     | Agent/capability registration + authoring              | `CAP-`  | — (empty)     | `ab-agent-registration`, `ab-capability-authoring` | Scaffold  |
| `in-chat-features`       | Approvals, clarification, generated UI, chips          | `FEAT-` | — (empty)     | `ab-in-chat-features`                              | Scaffold  |
| `tools-middleware`       | Tool/MCP invoke, turn pipeline, cost control           | `TOOL-` | — (empty)     | `ab-tool-registration`, `ab-middleware-authoring`  | Scaffold  |
| `ui-theming-integration` | CSS coexistence, theming                               | `THEM-` | — (empty)     | `ab-ui-integration`, `ab-mud-components`           | Scaffold  |
| `cli-tooling`            | CLI analyze/scaffold/doctor/validate                   | `CLI-`  | — (empty)     | `ab-cli`                                           | Scaffold  |
