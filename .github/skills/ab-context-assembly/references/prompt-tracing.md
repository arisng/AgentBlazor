# Prompt Tracing — Observing What the LLM Receives

Enable and use prompt tracing from a consumer app to debug what context was actually assembled and sent to the LLM. Tracing is opt-in and surfaced through the built-in inspector UI.

## Table of contents

- [Prompt Tracing — Observing What the LLM Receives](#prompt-tracing--observing-what-the-llm-receives)
  - [Table of contents](#table-of-contents)
  - [Enable tracing](#enable-tracing)
  - [Configuration options](#configuration-options)
  - [Viewing traces](#viewing-traces)
    - [Enable the inspector](#enable-the-inspector)
    - [What you can see](#what-you-can-see)
  - [What tracing captures](#what-tracing-captures)
  - [Storage and retention](#storage-and-retention)
    - [Implementing a subscriber](#implementing-a-subscriber)
  - [Troubleshooting](#troubleshooting)

---

## Enable tracing

One line in `Program.cs`:

```csharp
builder.AddAgentBlazor(options =>
{
    // ... provider, tools, agents ...

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.EnablePromptTracing(); // on with defaults
    });
});
```

With custom configuration:

```csharp
agentBuilder.EnablePromptTracing(options =>
{
    options.MaxTraceCount = 500;
    options.TraceRetention = TimeSpan.FromMinutes(30);
    options.CaptureFullContent = true;
});
```

Tracing is **off by default**. When disabled, there is zero overhead.

---

## Configuration options

All properties on `PromptTracingOptions`:

| Property | Type | Default | Purpose |
|---|---|---|---|
| `Enabled` | `bool` | `false` | Master switch — set to `true` by calling `EnablePromptTracing()` |
| `MaxTraceCount` | `int` | `1000` | Max traces retained in memory |
| `TraceRetention` | `TimeSpan` | `1 hour` | How long traces live before cleanup |
| `CaptureFullContent` | `bool` | `true` | Whether to capture full request/response text |
| `CaptureExtractedEntities` | `bool` | `true` | Whether to capture entities from intent classification |
| `CaptureActionArguments` | `bool` | `true` | Whether to capture tool/action arguments (may contain sensitive data) |
| `MaxContentLength` | `int` | `10000` | Truncation limit for captured content (characters) |
| `EnableAutoCleanup` | `bool` | `true` | Whether expired traces are automatically purged |
| `CleanupInterval` | `TimeSpan` | `5 minutes` | How often cleanup runs |

---

## Viewing traces

The **Agent Inspector** panel in the chat UI shows trace data when dev tools are enabled.

### Enable the inspector

On your `AgentChatSurface` or `AgentChatWidget` component:

```razor
<AgentChatSurface ShowDevTools="true" />
```

The inspector has a **"Prompt" tab** that shows the assembled system prompt for each run — exactly what was sent to the LLM.

### What you can see

- The full system instructions text (your `WithInstructions` string + any appended data schemas)
- Per-run metadata (session, user, agent)
- Execution details (planned actions, results, timing)

> **Note**: The inspector shows the prompt through the public inspector API. The trace storage and query infrastructure is internal to the package and not directly accessible from consumer code. Use the inspector UI or `IAgentRuntimeEventSubscriber` for programmatic observation.

---

## What tracing captures

Each trace records a full turn lifecycle across these stages:

| Stage | What's captured |
|---|---|
| **Entry** | Trace ID, timestamp, session ID, user ID, the raw user message, the context dictionary |
| **Classification** | Intent classification method, primary intent, confidence, extracted entities, timing |
| **Planning** | Which actions were planned, tool count, selection reasoning, timing |
| **Execution** | Per-action results (succeeded/failed, output, duration), overall success/failure counts |
| **Response** | The final response text, outcome (Succeeded / PartialSuccess / Failed / Canceled / Error), total turn duration |

---

## Storage and retention

- Traces are stored **in memory only** (per process, not persisted to disk).
- Controlled by `MaxTraceCount` and `TraceRetention`.
- When `EnableAutoCleanup` is on, expired traces are purged on `CleanupInterval`.
- Restarting the app clears all traces.

For production observability, use `IAgentRuntimeEventSubscriber` to forward turn events to your own telemetry or logging system.

### Implementing a subscriber

```csharp
public sealed class TelemetryEventSubscriber : IAgentRuntimeEventSubscriber
{
    private readonly ILogger<TelemetryEventSubscriber> _logger;

    public TelemetryEventSubscriber(ILogger<TelemetryEventSubscriber> logger)
    {
        _logger = logger;
    }

    public ValueTask OnTurnStartedAsync(
        AgentRuntimeTurnStartedEvent runtimeEvent,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Turn started: Session={Session}, Agent={Agent}",
            runtimeEvent.SessionId, runtimeEvent.AgentName);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnTurnFinishedAsync(
        AgentRuntimeTurnFinishedEvent runtimeEvent,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Turn finished: Agent={Agent}, Tokens={Tokens}",
            runtimeEvent.AgentName,
            runtimeEvent.Response.Usage?.TotalTokenCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(
        AgentRuntimeErrorEvent runtimeEvent,
        CancellationToken ct = default)
    {
        _logger.LogError(
            "Turn error: Agent={Agent}, Error={Message}",
            runtimeEvent.AgentName, runtimeEvent.ErrorMessage);
        return ValueTask.CompletedTask;
    }
}
```

Register it in `Program.cs`:

```csharp
builder.AddAgentBlazor(options =>
{
    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddRuntimeEventSubscriber<TelemetryEventSubscriber>();
    });
});
```

> **Note:** All methods on `IAgentRuntimeEventSubscriber` have default no-op implementations, so you only need to override the events you care about. The interface defines 5 methods: `OnTurnStartedAsync`, `OnTurnFinishedAsync`, `OnToolExecutionStartedAsync`, `OnToolExecutionFinishedAsync`, and `OnErrorAsync`.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Inspector "Prompt" tab is empty | `EnablePromptTracing()` was not called, or `ShowDevTools` is not enabled on the chat component |
| Traces disappear quickly | `TraceRetention` is too short — increase it |
| Old traces aren't being cleaned up | `EnableAutoCleanup` may be `false`, or `CleanupInterval` hasn't elapsed yet |
| Content is truncated | `MaxContentLength` limit reached — increase or disable truncation |
| Missing stage data (e.g., classification) | The stage may not have fired (e.g., classification skipped for direct chat turns); this is normal |
| Memory usage growing | Reduce `MaxTraceCount` or `TraceRetention`; disable `CaptureFullContent` if not needed |