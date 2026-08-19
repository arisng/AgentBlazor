# **Enterprise Context Management and Token Governance in .NET AI Agents: Operationalizing Per-Session Cost Tracking, Dynamic Interception, and Session Persistence**

The rapid enterprise adoption of autonomous AI agents demands strict operational governance over model interactions, context window allocations, and resource expenditures1. When building agentic architectures in C# and .NET, developers rely on the unified capabilities provided by the Microsoft Agent Framework (Microsoft.Agents.AI) in conjunction with Microsoft.Extensions.AI1. While conversational endpoints simply process isolated requests, autonomous agents run multi-step reasoning loops, execute external tool functions, and maintain long-lived conversation histories1. Without deterministic context management and granular cost instrumentation, these agents are susceptible to sudden context truncation, unbounded monetary burn, and state degradation across multi-turn workflows6.  
Managing an enterprise agent workspace requires treating the Large Language Model (LLM) context window as a structured computational memory buffer7. Achieving enterprise readiness requires a unified framework for real-time per-session token cost tracking, category-based context window quota allocations, and persistent state lifecycle management using differential text compaction6.

## **Architecture of Enterprise .NET Agents and Interception Boundaries**

The architecture of modern .NET agent systems relies on a two-tier abstraction model1. At the foundation sits Microsoft.Extensions.AI, which provides unified primitives such as IChatClient and AIFunction1. The higher-level orchestration layer, Microsoft.Agents.AI, builds upon these primitives to supply the AIAgent base class, session state persistence, and workflow choreography across multi-agent environments3. This abstraction enables application logic to remain agnostic of the underlying model host, supporting Azure OpenAI, OpenAI, Anthropic, Ollama, and custom endpoints without refactoring core business logic1.  
To enforce governance across agent workflows, the framework exposes a layered middleware pipeline operating across four distinct execution boundaries13. Agent-level middleware wraps the global agent execution, capturing top-level inputs and outputs during invocation13. Run-level middleware targets specific agent execution instances, enabling transient request customization and per-turn context injection13. Function-calling middleware intercepts tool invocations before and after local C# method execution, enabling schema transformations, argument sanitization, and permission enforcement13. Finally, IChatClient middleware—implemented via DelegatingChatClient—sits directly above the network layer, capturing raw payload exchanges, token usage telemetry, and low-level model responses6.  
The execution flow across these middleware layers follows a nested wrapping pattern13. Outer agent-level middleware wraps inner run-level middleware, which in turn wraps function calling and client middleware13. When multiple middleware callbacks are registered, they execute in a pipeline sequence: the first registered agent middleware executes before proceeding inward to run middleware, function handlers, and model invocation13. Upon completion, the return execution travels backward through the middleware chain in reverse order, allowing post-processing and metric aggregation at every boundary13.  
Tools are exposed to agents by converting local C# methods into executable AIFunction instances using AIFunctionFactory.Create()5. The factory reflects over method signatures, delegates, or MethodInfo targets to automatically derive JSON Schema parameter contracts and return types5. Annotations provided via System.ComponentModel.DescriptionAttribute populate metadata descriptions that guide model tool selection5. Specialized parameter types receive native framework handling during invocation: CancellationToken parameters automatically bind to the active token of the invocation context, while IServiceProvider parameters resolve dependencies directly from the application's dependency injection container without leaking internal implementation details into the public model schema15.

## **Dynamic Per-Session Token Cost Tracking Architecture**

Accurate financial reporting and budget enforcement require precise runtime accounting of token usage6. Relying on string-length estimations or character heuristics yields inaccurate results due to variations in Byte Pair Encoding (BPE) tokenization across different languages, code fragments, and structural JSON payloads6. The standard for financial accounting in enterprise .NET AI applications requires extracting actual usage metrics directly from the model provider's response payload via a DelegatingChatClient pipeline6.  
When the model completes an inference turn, the provider populates a UsageDetails or ChatTokenUsage object containing exact counts for input tokens, output tokens, and cached input tokens6. By decorating the IChatClient instance with a custom DelegatingChatClient, the application intercepts every completion event, calculates financial overhead based on current tier rates, and publishes structured audit telemetry to centralized monitoring sinks like Azure Event Hubs or OpenTelemetry collectors6.  

```LaTeX
$$
\text{Cost}_{\text{turn}} = \left( \frac{T_{\text{input}} - T_{\text{cached}}}{1000} \right) \times P_{\text{input}} + \left( \frac{T_{\text{cached}}}{1000} \right) \times P_{\text{cached}} + \left( \frac{T_{\text{output}}}{1000} \right) \times P_{\text{output}}
$$

$$
\text{Cost}_{\text{session}} = \sum_{k=1}^{N} \text{Cost}_{\text{turn},k}
$$
```

Where T_input represents total prompt tokens, T_cached represents prompt tokens served directly from the provider's context cache, T_output denotes generated completion tokens, and P signifies the unit price per thousand tokens for the corresponding billing category.  
Handling streaming model responses via GetStreamingResponseAsync requires specialized aggregation logic12. Because intermediate streaming chunks contain partial textual updates without usage metrics, the DelegatingChatClient must monitor the stream until the final sequence update, which carries the consolidated Usage payload6. Capturing this final update ensures that streaming interactions are audited with the same financial accuracy as synchronous requests6.

| Model Deployment Tier | Input Rate (Per 1,000 Tokens) | Cached Input Rate (Per 1,000 Tokens) | Output Rate (Per 1,000 Tokens) | Estimated Cost per 10-Turn Session (100k Input / 15k Output) |
| :---- | :---- | :---- | :---- | :---- |
| **GPT-4o (Standard Global)** | $0.00250 | $0.00125 | $0.01000 | $0.40000 |
| **GPT-4o-mini (Lightweight Operations)** | $0.00015 | $0.000075 | $0.00060 | $0.02400 |
| **Claude 3.5 Sonnet (Provider Proxy)** | $0.00300 | $0.00030 | $0.01500 | $0.52500 |
| **Azure OpenAI Provisioned Throughput (PTU)** | $0.00000 (Flat-rate) | $0.00000 (Flat-rate) | $0.00000 (Flat-rate) | Allocation Compute Fixed |

## **Concrete Context Quota Allocations Across Research and Execution Scope**

Maintaining reliability across long-running agentic research and tool execution loops requires dividing the LLM's context window into strict functional quotas7. Unconstrained expansion of any single context component—such as verbose vector search retrievals or expanded tool schemas—can saturate the context window, causing prompt truncation or unexpected model behavior7.  
An enterprise agent context window is partitioned into six distinct structural categories, each serving a specific role in the agent's operational cycle7:

> 1. **System Instructions and Guardrails**: Fixed system prompts defining agent persona, behavioral boundaries, and corporate compliance rules1.  
> 2. **Dynamic Tool Schemas**: Parameter schemas derived via AIFunctionFactory for all functions currently registered with the agent5.  
> 3. **Retrieved Context (RAG & Knowledge Graph)**: External domain documentation, vector search results, and database records retrieved to ground the conversation3.  
> 4. **Session History (AgentSession)**: The multi-turn conversation record containing past user queries, assistant responses, and historical tool call interactions9.  
> 5. **Scratchpad and Reasoning Workspace**: Intermediate workspace containing step-by-step reasoning chains, draft plan updates, and temporary execution traces3.  
> 6. **Output Generation Reserve**: Unreserved token capacity strictly preserved to allow the model to generate full structural completions without running out of tokens8.

| Context Quota Category | Allocation Strategy | Capacity Quota (128,000 Token Ceiling) | Capacity Quota (32,000 Token Ceiling) | Eviction and Compaction Cascade Priority |
| :---- | :---- | :---- | :---- | :---- |
| **System Instructions & Guardrails** | Immutable Static Anchor | 5,000 tokens (3.9%) | 2,000 tokens (6.25%) | Non-evictable (Static baseline) |
| **Dynamic Tool Schemas** | Relevance-Filtered Schema | 10,000 tokens (7.8%) | 4,000 tokens (12.50%) | High: Filter tools by domain relevance |
| **Retrieved Context (RAG)** | Reranked Sliding Window | 45,000 tokens (35.2%) | 10,000 tokens (31.25%) | Medium: Evict low-scoring passages |
| **Session History** | Compacted / Summarized | 40,000 tokens (31.2%) | 10,000 tokens (31.25%) | Medium: Apply differential patching |
| **Scratchpad & Workspace** | Turn-Purged State | 12,000 tokens (9.4%) | 3,000 tokens (9.38%) | Critical: Flush post-step completion |
| **Output Generation Reserve** | Enforced Safety Ceiling | 16,000 tokens (12.5%) | 3,000 tokens (9.38%) | Absolute: Protected generation space |

To enforce these allocations at runtime, the application uses tokenizing engines like Microsoft.ML.Tokenizers or tiktoken to measure incoming payloads prior to model invocation8. The total token consumption across all active categories must satisfy the structural constraint:  

```LaTeX
$$
T_{\text{total}} = T_{\text{system}} + \sum T_{\text{tools}} + T_{\text{rag}} + T_{\text{history}} + T_{\text{scratchpad}} \leq C_{\text{window}} - R_{\text{generation}}
$$
```

When total token requirements exceed safe context limits, the system initiates a context eviction cascade7. First, transient scratchpad entries from completed execution steps are cleared7. Next, retrieved RAG passages are filtered using Reciprocal Rank Fusion (RRF) scores to drop low-relevance content7. Finally, if context usage remains above acceptable thresholds, the session history undergoes differential compaction, converting long multi-turn transcript sequences into structured summaries or diff patches7.

## **Session State Persistence, Lifecycle Management, and Differential Context Compaction**

Enterprise agent deployments require state continuity across stateless infrastructure, background worker instances, and multi-region deployment nodes2. The Microsoft Agent Framework manages conversation state using the AgentSession abstraction9. This mechanism tracks multi-turn execution histories, pending tool invocations, and custom operational metadata9.  
The framework provides two primary session storage models to support different deployment architectures9:

* **InMemoryAgentSession**: Holds conversation state in process memory9. It provides low latency for single-node deployments and supports JSON serialization via SerializeSessionAsync for persistence across process restarts9.  
* **ServiceIdAgentSession**: Decouples session state from local application memory by associating interactions with external data stores such as Azure Cosmos DB, Redis, or SQL Server9.

As multi-turn conversations progress, maintaining full conversational histories in context becomes token-prohibitive7. When an agent repeatedly reads, modifies, and outputs large text files or source code, re-transmitting full document copies on every turn rapidly consumes allocated quotas7. To address this challenge, agents can integrate differential text processing using libraries like DiffPlex18.  
Using DiffPlex.DiffBuilder.SideBySideDiffBuilder and the core Differ engine, the agent compares document baselines against updated states, identifying specific line-by-line additions, deletions, and modifications18. Rather than appending a complete 20,000-token file back into the AgentSession history buffer, the framework constructs a lightweight, diff-encoded patch7. This patch captures structural changes using precise line placement markers, reducing context consumption while giving the model the exact information required for subsequent reasoning turns7.

## **Production C# Governance and Resilience Implementation**

The following complete C# implementation demonstrates an enterprise agent setup incorporating custom DelegatingChatClient accounting, AIFunctionFactory tool generation, context boundary checking, and session lifecycle orchestration6.

```csharp
using System.ComponentModel;  
using System.Diagnostics;  
using System.Text.Json;  
using Microsoft.Agents.AI;  
using Microsoft.Extensions.AI;  
using Microsoft.Extensions.Logging;

// -----------------------------------------------------------------------------  
// 1. Telemetry Data Models & Audit Repositories  
// -----------------------------------------------------------------------------  
public record SessionTokenMetric(  
    string SessionId,  
    string TenantId,  
    string ModelId,  
    long PromptTokens,  
    long CompletionTokens,  
    decimal CalculatedCostUsd,  
    long LatencyMilliseconds,  
    DateTime TimestampUtc  
);

public interface ISessionCostRepository  
{  
    Task RecordUsageAsync(SessionTokenMetric metric, CancellationToken ct = default);  
}

public class ConsoleCostRepository : ISessionCostRepository  
{  
    private readonly ILogger<ConsoleCostRepository> _logger;  
    public ConsoleCostRepository(ILogger<ConsoleCostRepository> logger) => _logger = logger;

    public Task RecordUsageAsync(SessionTokenMetric metric, CancellationToken ct = default)  
    {  
        _logger.LogInformation(  
            "[AUDIT] Session: {SessionId} | Model: {Model} | Prompt Tokens: {Prompt} | Completion Tokens: {Completion} | Cost: ${Cost:F6} | Latency: {Latency}ms",  
            metric.SessionId, metric.ModelId, metric.PromptTokens, metric.CompletionTokens, metric.CalculatedCostUsd, metric.LatencyMilliseconds  
        );  
        return Task.CompletedTask;  
    }  
}

// -----------------------------------------------------------------------------  
// 2. Enterprise Governance IChatClient Interceptor  
// -----------------------------------------------------------------------------  
public class EnterpriseGovernanceChatClient : DelegatingChatClient  
{  
    private readonly ISessionCostRepository _costRepository;  
    private const int PromptTokenBoundaryLimit = 112000; // Leaves safety buffer for generation

    public EnterpriseGovernanceChatClient(IChatClient innerClient, ISessionCostRepository costRepository)   
        : base(innerClient)  
    {  
        _costRepository = costRepository;  
    }

    public override async Task<ChatResponse> GetResponseAsync(  
        IList<ChatMessage> chatMessages,   
        ChatOptions? options = null,   
        CancellationToken cancellationToken = default)  
    {  
        EnforceContextBoundaries(chatMessages);

        var stopwatch = Stopwatch.StartNew();  
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);  
        stopwatch.Stop();

        if (response.Usage \!= null)  
        {  
            var sessionId = options?.AdditionalProperties?.TryGetValue("SessionId", out var sid) == true   
                ? sid.ToString()\!   
                : "UnassignedSession";

            var tenantId = options?.AdditionalProperties?.TryGetValue("TenantId", out var tid) == true   
                ? tid.ToString()\!   
                : "DefaultTenant";

            var modelId = options?.ModelId ?? "gpt-4o-mini";

            long promptTokens = response.Usage.InputTokenCount ?? 0;  
            long completionTokens = response.Usage.OutputTokenCount ?? 0;

            decimal inputRate = modelId.Contains("mini") ? 0.00015m / 1000m : 0.0025m / 1000m;  
            decimal outputRate = modelId.Contains("mini") ? 0.00060m / 1000m : 0.0100m / 1000m;

            decimal totalCalculatedCost = (promptTokens \* inputRate) \+ (completionTokens \* outputRate);

            var metric = new SessionTokenMetric(  
                SessionId: sessionId,  
                TenantId: tenantId,  
                ModelId: modelId,  
                PromptTokens: promptTokens,  
                CompletionTokens: completionTokens,  
                CalculatedCostUsd: totalCalculatedCost,  
                LatencyMilliseconds: stopwatch.ElapsedMilliseconds,  
                TimestampUtc: DateTime.UtcNow  
            );

            await _costRepository.RecordUsageAsync(metric, cancellationToken);  
        }

        return response;  
    }

    private static void EnforceContextBoundaries(IList<ChatMessage> messages)  
    {  
        int estimatedTokenCount = 0;  
        foreach (var message in messages)  
        {  
            estimatedTokenCount += (message.Text?.Length ?? 0) / 4;  
        }

        if (estimatedTokenCount > PromptTokenBoundaryLimit)  
        {  
            throw new InvalidOperationException(  
                $"Context Quota Exceeded: Estimated prompt size ({estimatedTokenCount} tokens) exceeds boundary limit of {PromptTokenBoundaryLimit} tokens."  
            );  
        }  
    }  
}

// -----------------------------------------------------------------------------  
// 3. Business Function Tools & Execution Pipeline  
// -----------------------------------------------------------------------------  
public static class CorporateLedgerTools  
{  
    [Description("Retrieves validated ledger balance details for audit verification.")]  
    public static string GetLedgerBalance(  
        [Description("The corporate account identifier string, e.g., 'ACC-7741'")] string accountId)  
    {  
        return accountId switch  
        {  
            "ACC-7741" => JsonSerializer.Serialize(new { AccountId = "ACC-7741", Balance = 2845000.00, Status = "Active" }),  
            _ => JsonSerializer.Serialize(new { AccountId = accountId, Balance = 0.00, Status = "NotFound" })  
        };  
    }  
}

public class CorporateAgentHost  
{  
    private readonly AIAgent _agent;  
    private readonly ISessionCostRepository _costRepository;

    public CorporateAgentHost(IChatClient baseChatClient, ISessionCostRepository costRepository)  
    {  
        _costRepository = costRepository;

        // Wrap underlying IChatClient with governance middleware  
        IChatClient governedClient = new EnterpriseGovernanceChatClient(baseChatClient, _costRepository);

        // Convert static C# business logic into an executable AIFunction  
        AIFunction ledgerTool = AIFunctionFactory.Create(CorporateLedgerTools.GetLedgerBalance);

        // Instantiates the governed AIAgent  
        _agent = governedClient.CreateAIAgent(  
            name: "CorporateAuditor",  
            instructions: "You are an enterprise financial auditing agent. Verify ledger balances using function tools.",  
            tools: new[] { ledgerTool }  
        );  
    }

    public async Task RunAuditCycleAsync(string sessionId, string userPrompt)  
    {  
        // Instantiates session tracking multi-turn context  
        AgentSession session = await _agent.CreateSessionAsync();

        var runOptions = new ChatOptions  
        {  
            ModelId = "gpt-4o-mini",  
            AdditionalProperties = new ChatOptionsPropertyDictionary  
            {  
                { "SessionId", sessionId },  
                { "TenantId", "Finance-Audit-Division" }  
            }  
        };

        // Executes turn through the governed pipeline  
        var response = await _agent.RunAsync(userPrompt, session, options: runOptions);  
        Console.WriteLine($"Agent Output: {response}");

        // Demonstrates session state persistence serialization  
        JsonElement serializedSessionState = await session.SerializeSessionAsync();  
        Console.WriteLine($"Session Persisted. State Size: {serializedSessionState.GetRawText().Length} bytes.");  
    }  
}
```

## **Strategic Directives for Enterprise Operations**

Operating autonomous agents in production requires establishing clear boundaries between reasoning capabilities, context management, and fiscal controls1. Organizations deploying the Microsoft Agent Framework and Microsoft.Extensions.AI should follow key operational guidelines to ensure system stability and cost predictability3.  
Centralizing token accounting at the network boundary using standard DelegatingChatClient implementations ensures that usage monitoring cannot be bypassed by agent code or tool execution loops6. Capturing actual provider metrics rather than relying on local string length estimates guarantees accurate billing, audit compliance, and cost tracking across all tenant sessions6.  
Similarly, context allocation budgets must be enforced dynamically before model requests are dispatched7. Defining strict quotas across system prompts, dynamic tool schemas, retrieved context, and multi-turn history prevents unexpected context window saturation5. When capacity limits are approached, applying automated eviction cascades—clearing temporary workspace entries, reranking retrieved documentation, and compacting conversation histories via differential patching—maintains operational continuity without exceeding token ceilings7.  
Finally, session persistence must be decoupled from application process memory9. Utilizing ServiceIdAgentSession or backing up session state using SerializeSessionAsync allows agents to resume multi-turn interactions across distributed serverless nodes, container upgrades, and system failovers2. Combining persistent session architectures with real-time token instrumentation provides the foundation required for reliable, cost-controlled, enterprise-grade AI agent systems1.

#### **Works cited**

> 1. Building Your First AI Agent in C# with Microsoft Agent Framework - DEV Community, [https://dev.to/matteo_davena/building-your-first-ai-agent-in-c-with-microsoft-agent-framework-i33](https://dev.to/matteo_davena/building-your-first-ai-agent-in-c-with-microsoft-agent-framework-i33)  
> 2. Developing AI Agents in .NET Web API using Microsoft Agent Framework - Medium, [https://mehmetozkaya.medium.com/developing-ai-agents-in-net-web-api-using-microsoft-agent-framework-9eca93f1bbb0](https://mehmetozkaya.medium.com/developing-ai-agents-in-net-web-api-using-microsoft-agent-framework-9eca93f1bbb0)  
> 3. Microsoft Agent Framework Overview, [https://learn.microsoft.com/en-us/agent-framework/overview/](https://learn.microsoft.com/en-us/agent-framework/overview/)  
> 4. GitHub - microsoft/agent-framework: A framework for building, orchestrating and deploying AI agents and multi-agent workflows with support for Python and .NET., [https://github.com/microsoft/agent-framework](https://github.com/microsoft/agent-framework)  
> 5. Function Tools with AIFunctionFactory in Microsoft Agent Framework - Dev Leader, [https://www.devleader.ca/2026/02/21/function-tools-with-aifunctionfactory-in-microsoft-agent-framework](https://www.devleader.ca/2026/02/21/function-tools-with-aifunctionfactory-in-microsoft-agent-framework)  
> 6. Building Enterprise AI Audit Trails with Microsoft Fabric - C# Corner, [https://www.c-sharpcorner.com/article/building-enterprise-ai-audit-trails-with-microsoft-fabric/](https://www.c-sharpcorner.com/article/building-enterprise-ai-audit-trails-with-microsoft-fabric/)  
> 7. POC template for Microsoft Agent Framework agentic harness — skills, MCP, tools system modeled after Claude Code architecture - GitHub, [https://github.com/MCKRUZ/microsoft-agentic-harness](https://github.com/MCKRUZ/microsoft-agentic-harness)  
> 8. LLM Context Windows Explained: Token Budget Guide - machinelearningplus, [https://machinelearningplus.com/gen-ai/context-windows-token-budget/](https://machinelearningplus.com/gen-ai/context-windows-token-budget/)  
> 9. Custom Agents | Microsoft Learn, [https://learn.microsoft.com/en-us/agent-framework/concepts/agents/custom-agents](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/custom-agents)  
> 10. Learn-Microsoft-Agent-Framework-with-Foundry-ZavaShop-Supply-Chain-Workshop/.github/skills/agent-framework-azure-ai-csharp/references/threads.md at main · microsoft/Learn-Microsoft-Agent-Framework-with-Foundry-ZavaShop-Supply-Chain-, [https://github.com/microsoft/Learn-Microsoft-Agent-Framework-with-Foundry-ZavaShop-Supply-Chain-Workshop/blob/main/.github/skills/agent-framework-azure-ai-csharp/references/threads.md](https://github.com/microsoft/Learn-Microsoft-Agent-Framework-with-Foundry-ZavaShop-Supply-Chain-Workshop/blob/main/.github/skills/agent-framework-azure-ai-csharp/references/threads.md)  
> 11. [Microsoft.Extensions.AI] A way to dynamically provide Tools (AIFunctions) \#6526 - GitHub, [https://github.com/dotnet/extensions/discussions/6526](https://github.com/dotnet/extensions/discussions/6526)  
> 12. Microsoft.Extensions.AI.OpenAI 10.9.0 - NuGet, [https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI?ref=codetraveler.io](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI?ref=codetraveler.io)  
> 13. Agent Middleware - Microsoft Learn, [https://learn.microsoft.com/en-us/agent-framework/concepts/agents/middleware/](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/middleware/)  
> 14. Using function tools with an agent | Microsoft Learn, [https://learn.microsoft.com/vi-vn/agent-framework/agents/tools/function-tools?pivots=programming-language-csharp](https://learn.microsoft.com/vi-vn/agent-framework/agents/tools/function-tools?pivots=programming-language-csharp)  
> 15. AIFunctionFactory.Create Method (Microsoft.Extensions.AI), [https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.aifunctionfactory.create?view=net-11.0-pp](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.aifunctionfactory.create?view=net-11.0-pp)  
> 16. AIFunctionFactory Class (Microsoft.Extensions.AI), [https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.aifunctionfactory?view=net-11.0-pp](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.aifunctionfactory?view=net-11.0-pp)  
> 17. Microsoft.ML.Tokenizers Namespace, [https://learn.microsoft.com/vi-vn/dotnet/api/microsoft.ml.tokenizers?view=ml-dotnet-2.0.0](https://learn.microsoft.com/vi-vn/dotnet/api/microsoft.ml.tokenizers?view=ml-dotnet-2.0.0)  
> 18. diffplex/DiffPlex.Wpf/Controls/SideBySideDiffViewer.xaml.cs at master · mmanela/diffplex · GitHub, [https://github.com/mmanela/diffplex/blob/master/DiffPlex.Wpf/Controls/SideBySideDiffViewer.xaml.cs](https://github.com/mmanela/diffplex/blob/master/DiffPlex.Wpf/Controls/SideBySideDiffViewer.xaml.cs)  
> 19. C# DiffPlex介绍-CSDN博客 - Asp.net教程, [https://www.shaoqun.com/a/2518327.html](https://www.shaoqun.com/a/2518327.html)  
> 20. WPFでGitのDiffっぽい差分表示をするDiffPlexライブラリ。～あるいは、あるジェダイの変遷 - Qiita, [https://qiita.com/soi/items/ae880d73377b13322ec7](https://qiita.com/soi/items/ae880d73377b13322ec7)  
> 21. C#／WPFで変更前後の差分表示のできる、ファイル名変更ソフトの作り方 - Qiita, [https://qiita.com/soi/items/aa82eb68577d339dd6af](https://qiita.com/soi/items/aa82eb68577d339dd6af)
