# Layout & Navigation

> **ab\* skill**: `ab-mud-components` | **Status**: ✅ implemented

## What it is

- **`AgentTreeView`** — a hierarchical tree the agent can expand, collapse, and select nodes in.
- **`AgentStepper`** — a step-by-step wizard the agent can advance or rewind.
- **`AgentTabs`** — tabbed navigation the agent can switch between tabs.
- **`AgentNavMenu`** — a navigation menu the agent can drive.

## Why this matters

Complex workflows often involve hierarchical data (trees), multi-step processes
(steppers), and different views of the same context (tabs). Making these agent-
controllable means the agent can navigate the structure for you — expanding the right
tree node, advancing to the next step, or switching to the tab with the information
you asked about. Instead of the user manually clicking through 5 steps to get to the
right view, the agent takes them there in one command.

## Where to find it in the Demo

- `/demo/components` — Interactive playground for all four
- `/demo/workflows/incident-escalation` — `AgentTreeView`, `AgentStepper`, and `AgentTabs`
  used in the incident triage workflow

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/components`.
3. **TreeView**: type `Expand all nodes in the tree`.
4. **Stepper**: type `Go to the next step` or `Go back to step 1`.
5. **Tabs**: type `Switch to the second tab`.
6. **NavMenu**: type `Navigate to the settings page`.
7. Navigate to `/demo/workflows/incident-escalation` to see these components
   used in a real workflow context.

## What to observe

- The tree expands/collapses nodes based on agent commands.
- The stepper advances through steps and can go backward.
- Tabs switch to the agent-selected tab.
- Navigation components reflect the agent's state changes immediately.

## Related features

- [Command Bar & File Upload](command-bar-file-upload.md) — action bar components
- [Workflow: Incident Escalation](../workflows/incident-escalation.md) — workflow using these components
