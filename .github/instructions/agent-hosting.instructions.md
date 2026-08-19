---
description: "Agent hosting patterns for AgentBlazor. Use when configuring AgentBlazor services, setting up providers, or integrating with ASP.NET Core. Covers service registration, provider configuration, and endpoint mapping."
applyTo: "src/AgentBlazor.Hosting/**/*.cs"
---

# Agent Hosting Patterns

## Overview

AgentBlazor provides ASP.NET Core hosting integration for registering agent services, configuring providers, and mapping endpoints.

## Service Registration

1. **AddAgentBlazor**: Register AgentBlazor services
2. **ConfigureBuilder**: Configure agent registration
3. **ConfigureChatOptions**: Pin provider-level ChatOptions

## Provider Configuration

1. **UseOpenAI**: Configure OpenAI provider
2. **UseAzureOpenAI**: Configure Azure OpenAI provider
3. **UseOllama**: Configure Ollama provider
4. **UseOriginAI**: Configure OriginAI provider

## Conventions

1. **Extension methods**: Use extension methods for service registration
2. **Builder pattern**: Use `AgentBlazorBuilder` for configuration
3. **Options pattern**: Use `AgentBlazorRegistrationOptions` for options
4. **Endpoint mapping**: Use `MapAgentBlazorEndpoints()` for AG-UI endpoints

## Best Practices

1. **Provider seam**: Use `ConfigureChatOptions` for provider-level option pinning
2. **Middleware pipeline**: Use `UseMiddleware` for cross-cutting concerns
3. **Tool registration**: Use `AddTool` for custom tools
4. **MCP integration**: Use `UseMcpServer` for MCP server integration

## Example

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"]!);

    options.ConfigureChatOptions(o => 
        o.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None });

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<MyCapabilities>("my-workflow", agent =>
        {
            agent.WithRoutePrefixes("/my-prefix");
        });
    });
});

var app = builder.Build();
app.MapAgentBlazorEndpoints();
```

## References

- See `src/AgentBlazor.Hosting/AgentBlazorRegistrationOptions.cs` for options
- See `src/AgentBlazor.Hosting/AgentBlazorUnifiedServiceCollectionExtensions.cs` for service registration
- See `.github/skills/ab-provider-config/` for provider configuration details