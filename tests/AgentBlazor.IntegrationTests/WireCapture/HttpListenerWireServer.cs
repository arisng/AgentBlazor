using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AgentBlazor.IntegrationTests.WireCapture;

/// <summary>
/// Minimal HTTP server (System.Net.HttpListener on a loopback port) that captures the raw
/// request bodies sent to it and serves canned OpenAI chat-completion responses.
///
/// This lets tests assert on the wire JSON produced by the AgentBlazor OpenAI provider path
/// (UseOpenAI → AddOpenAIProvider → OpenAIClient.GetChatClient(model).AsIChatClient()) without
/// any real API call — the same capture technique used to verify the GPT-5.6 reasoning_effort
/// bug in the consumer handoff (260814-agentblazor-gpt56-luna-reasoning-effort).
///
/// Behavior (decided per request body, so both streaming and non-streaming calls work):
///   1. Streaming ("stream":true)  → SSE chat.completion.chunk stream, terminated with data: [DONE].
///   2. Tool calls (tools present, no tool-role message yet, <see cref="ToolCallsEnabled"/>)
///      → responds with a tool_calls completion for the FIRST tool offered by the client
///      (the function name is echoed from the request, so the mock never guesses the tool name).
///   3. Otherwise → plain-text completion ("ok").
/// All requests are recorded via <see cref="RequestBodies"/> (in arrival order).
/// </summary>
public sealed class HttpListenerWireServer : IAsyncDisposable
{
    private const long CreatedEpoch = 1700000000;
    private const string CompletionId = "chatcmpl-wirecapture";

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serveLoop;
    private readonly ConcurrentQueue<string> _requestBodies = new();

    private HttpListenerWireServer(HttpListener listener, int port)
    {
        _listener = listener;
        BaseUrl = $"http://127.0.0.1:{port}/";
        // Provider endpoints are expected to carry a path (e.g. http://host/v1); the OpenAI SDK
        // appends /chat/completions to it. The listener prefix catches any path under the port.
        EndpointUrl = $"{BaseUrl}v1";
        _serveLoop = Task.Run(ServeLoopAsync);
    }

    /// <summary>Base URL of the listener, e.g. http://127.0.0.1:54321/</summary>
    public string BaseUrl { get; }

    /// <summary>Endpoint to pass to AddOpenAIProvider(..., endpoint); the SDK posts to {EndpointUrl}/chat/completions.</summary>
    public string EndpointUrl { get; }

    /// <summary>When true, requests carrying tools (and no tool-role message yet) get a tool_calls completion.</summary>
    public bool ToolCallsEnabled { get; set; } = true;

    /// <summary>Raw request bodies captured so far, in arrival order.</summary>
    public IReadOnlyList<string> RequestBodies => _requestBodies.ToArray();

    /// <summary>Starts a listener on a free loopback port. Loopback prefixes need no URL ACL on Windows.</summary>
    public static HttpListenerWireServer Start()
    {
        // http.sys rejects "port 0" in a prefix; learn a free port via TcpListener, then rebind.
        // The port could be taken between the probe and the bind, so retry a few times.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var port = FindFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                return new HttpListenerWireServer(listener, port);
            }
            catch (HttpListenerException) when (attempt < 4)
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException("Could not bind HttpListener to a free loopback port.");
    }

    private static int FindFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task ServeLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, _cts.Token));
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync(cancellationToken);
            }

            _requestBodies.Enqueue(body);

            if (IsStreamingRequest(body))
            {
                context.Response.ContentType = "text/event-stream";
                await WriteSseAsync(context.Response, body, cancellationToken);
            }
            else
            {
                context.Response.ContentType = "application/json";
                var payload = BuildJsonResponse(body);
                var bytes = Encoding.UTF8.GetBytes(payload);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            }

            context.Response.StatusCode = 200;
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception)
        {
            // A malformed request must never crash the capture server; tests assert on the body.
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception)
            {
            }
        }
    }

    private static bool IsStreamingRequest(string requestBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            return doc.RootElement.TryGetProperty("stream", out var stream) &&
                   stream.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasToolRoleMessage(string requestBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (!doc.RootElement.TryGetProperty("messages", out var messages))
            {
                return false;
            }

            foreach (var message in messages.EnumerateArray())
            {
                if (message.TryGetProperty("role", out var role) &&
                    string.Equals(role.GetString(), "tool", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static string? GetFirstOfferedToolName(string requestBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (!doc.RootElement.TryGetProperty("tools", out var tools))
            {
                return null;
            }

            foreach (var tool in tools.EnumerateArray())
            {
                if (tool.TryGetProperty("function", out var function) &&
                    function.TryGetProperty("name", out var name))
                {
                    return name.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private string BuildJsonResponse(string requestBody)
    {
        if (ToolCallsEnabled && !HasToolRoleMessage(requestBody) && GetFirstOfferedToolName(requestBody) is { } toolName)
        {
            return BuildToolCallsJson(toolName);
        }

        return BuildTextJson("ok");
    }

    private static string BuildTextJson(string content) =>
        $$"""
        {
          "id": "{{CompletionId}}",
          "object": "chat.completion",
          "created": {{CreatedEpoch}},
          "model": "gpt-4o-mini",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "{{content}}" },
              "finish_reason": "stop",
              "logprobs": null
            }
          ],
          "usage": { "prompt_tokens": 5, "completion_tokens": 5, "total_tokens": 10 }
        }
        """;

    private static string BuildToolCallsJson(string toolName) =>
        $$"""
        {
          "id": "{{CompletionId}}",
          "object": "chat.completion",
          "created": {{CreatedEpoch}},
          "model": "gpt-4o-mini",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": null,
                "tool_calls": [
                  {
                    "id": "call_wirecapture_1",
                    "type": "function",
                    "function": { "name": "{{toolName}}", "arguments": "{}" }
                  }
                ]
              },
              "finish_reason": "tool_calls",
              "logprobs": null
            }
          ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 10, "total_tokens": 20 }
        }
        """;

    private async Task WriteSseAsync(HttpListenerResponse response, string requestBody, CancellationToken cancellationToken)
    {
        var chunks = ToolCallsEnabled && !HasToolRoleMessage(requestBody) && GetFirstOfferedToolName(requestBody) is { } toolName
            ? BuildToolCallChunks(toolName)
            : BuildTextChunks("ok");

        var builder = new StringBuilder();
        foreach (var chunk in chunks)
        {
            builder.Append("data: ").Append(chunk).Append("\n\n");
        }

        builder.Append("data: [DONE]\n\n");

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private static IEnumerable<string> BuildTextChunks(string content)
    {
        yield return SseChunk(
            "\"delta\":{\"role\":\"assistant\",\"content\":\"" + content + "\"},\"finish_reason\":null");
        yield return SseChunk("\"delta\":{},\"finish_reason\":\"stop\"");
    }

    private static IEnumerable<string> BuildToolCallChunks(string toolName)
    {
        yield return SseChunk(
            "\"delta\":{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"id\":\"call_wirecapture_1\",\"type\":\"function\",\"function\":{\"name\":\"" + toolName + "\",\"arguments\":\"\"}}]},\"finish_reason\":null");
        yield return SseChunk(
            "\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{}\"}}]},\"finish_reason\":null");
        yield return SseChunk("\"delta\":{},\"finish_reason\":\"tool_calls\"");
    }

    private static string SseChunk(string choiceBody) =>
        "{\"id\":\"" + CompletionId + "\",\"object\":\"chat.completion.chunk\",\"created\":" + CreatedEpoch +
        ",\"model\":\"gpt-4o-mini\",\"choices\":[{\"index\":0," + choiceBody + "}]}";

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _listener.Close();
            await _serveLoop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
        }

        _cts.Dispose();
    }
}
