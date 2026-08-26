# Logging Middleware

> **ab\* skill**: `ab-middleware-authoring` | **Status**: ✅ implemented

## What it is

Two middleware components log activity to JSONL files:

- **`DemoChatRequestLoggingMiddleware`** — captures every chat request/response turn,
  including the prompt, response, timing, and agent metadata.
- **`DemoTrafficLoggingMiddleware`** — captures HTTP traffic (all endpoints, not just
  chat) with request/response metadata.

## Why this matters

You cannot debug what you cannot see. Without logging middleware, agent behavior is a
black box — you know the user sent a prompt and got a response, but you do not know
what the agent actually did, how long it took, or what went wrong. These middleware
capture every turn as structured JSONL data, giving you a complete audit trail for
debugging, performance tuning, and compliance. In production, this is the data you
use to answer "why did the agent do that?" and "how can we make it faster?"

## Where to find it in the Demo

- `Program.cs` → `options.UseMiddleware<DemoChatRequestLoggingMiddleware>()` (chat)
- `Program.cs` → `app.UseMiddleware<DemoTrafficLoggingMiddleware>()` (HTTP traffic)
- Log files are written to a JSONL output directory

## How to experience it

1. Start the Demo.
2. Navigate to `/demo/workflows/support-inbox`.
3. Type a prompt and observe the agent responding.
4. Navigate to `/internal/demo-logs/login` to authenticate, then `/internal/demo-logs/view` — the **log inspection web UI**.
5. You should see the chat request/response you just made in the log viewer.
6. Navigate to `/internal/demo-logs/traffic` to see HTTP traffic logs.

## What to observe

- Every chat turn is logged with timestamps, prompt text, response text, and metadata.
- HTTP traffic logs capture all endpoint calls, not just chat.
- Logs are append-only JSONL files (one JSON object per line).
- Log files can be downloaded from the log endpoints.

## Related features

- [Log Endpoints](log-endpoints.md) — web UI for viewing logs
- [Prompt Tracing](prompt-tracing.md) — deeper prompt pipeline tracing
