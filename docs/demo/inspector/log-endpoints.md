# Log Endpoints

> **ab\* skill**: `ab-middleware-authoring` | **Status**: ✅ implemented | **Demo**: 8 endpoints

## What it is

A set of HTTP endpoints that provide a **web UI for browsing and downloading** the
JSONL log files produced by the logging middleware. The endpoints are token-protected
(see login flow below).

## Why this matters

Raw JSONL files are useful for programmatic analysis, but sometimes you just want to
quickly see what happened. These log endpoints give you a browser-based viewer for
chat logs, traffic logs, and summary statistics — no terminal commands, no log parsers.
In development, this is how you verify that the middleware is capturing the right data.
In production, this is how support engineers can quickly review what an agent did
without digging through raw files.

## Where to find it in the Demo

Defined in `Services/DemoLogEndpointMapper.cs`, mapped under `/internal/demo-logs/`:

| Endpoint | Purpose |
|---|---|
| `/internal/demo-logs/login` | Authenticate (redirects to `/view`) |
| `/internal/demo-logs/logout` | Clear session |
| `/internal/demo-logs/view` | HTML viewer for chat request logs |
| `/internal/demo-logs/` | Raw JSONL stream of chat logs (`application/x-ndjson`) |
| `/internal/demo-logs/traffic` | HTML viewer for HTTP traffic logs |
| `/internal/demo-logs/summary` | JSON summary of log statistics |
| `/internal/demo-logs/download` | Download chat logs as JSONL |
| `/internal/demo-logs/traffic/download` | Download traffic logs as JSONL |

## How to experience it

1. Start the Demo.
2. Navigate to `/internal/demo-logs/login` to authenticate.
3. You are redirected to `/internal/demo-logs/view` — the chat log viewer.
4. Generate some activity: navigate to a workflow and send a few prompts.
5. Return to `/internal/demo-logs/view` — the new log entries appear.
6. Click **Summary JSON** (`/internal/demo-logs/summary`) for aggregated stats.
7. Click **Download chat** or **Download traffic** for raw JSONL files.

## What to observe

- Log entries show timestamp, prompt text, response text, agent name, and timing.
- The traffic log captures all HTTP requests, not just chat.
- The summary endpoint returns a JSON object with counts and timing statistics.
- Downloaded files are raw JSONL (one JSON object per line, parseable by any tool).

## Related features

- [Logging Middleware](logging-middleware.md) — produces the log data
- [Prompt Tracing](prompt-tracing.md) — deeper prompt pipeline tracing
