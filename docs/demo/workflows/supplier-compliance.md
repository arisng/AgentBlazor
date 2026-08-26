# Supplier Compliance

> **Agent**: Supplier Compliance Agent | **Capability**: `SupplierComplianceCapabilities` | **Route**: `/demo/workflows/supplier-compliance`

## What it is

A supplier audit and compliance workflow where the agent evaluates suppliers against
compliance criteria, identifies gaps, and triggers escalation. Demonstrates
clarification requests, data grid interaction, and approval-gated actions.

## Why this matters

Supplier compliance is a domain where the cost of a mistake is high — approving a
non-compliant supplier can lead to regulatory penalties or product defects. This
workflow shows how an agent can handle the tedious parts (loading supplier data,
running checks, drafting reports) while keeping a human in the loop for the
critical decisions (escalating, approving). It demonstrates that agents work best
in domains where there is structured data, repeatable processes, and clear approval
boundaries.

## Key features demonstrated

- `AgentDataGrid` — supplier listing
- `AgentDialog` — compliance report details
- Clarification requests — agent asks which supplier to audit
- Approval flows — escalation requires approval

## How to experience it

1. Navigate to `/demo/workflows/supplier-compliance`.
2. **View suppliers**: type `Show all suppliers`.
3. **Check compliance**: type `Check compliance for Acme Corp`.
4. **Audit**: type `Run a compliance audit` — agent may clarify which supplier.
5. **Escalate**: type `Escalate non-compliant suppliers` — approval dialog appears.

## Prompts to try

- `Show all suppliers`
- `Check compliance for Acme Corp`
- `Run a compliance audit`
- `Escalate non-compliant suppliers`
- `Reset the workflow`

## What to observe

- Supplier data renders in a grid with compliance status.
- The agent asks for clarification when the supplier target is ambiguous.
- Compliance checks produce structured results with pass/fail criteria.
- Escalation requires approval before executing.

## Related features

- [Clarification Requests](../capabilities/clarification-requests.md)
- [Approval Flows](../capabilities/approval-flows.md)
