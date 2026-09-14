using AgentBlazor;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Demo.Components;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using AgentBlazor.Core.Data;
using AgentBlazor.Core.Runtime.Tools;
using AgentBlazor.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

// Ensure [AgentFlow] logs are visible when running prompts
builder.Logging.AddFilter("AgentBlazor.Core.Runtime.Agents.AgentRuntime", LogLevel.Information);
builder.Logging.AddFilter("AgentBlazor.Core.Runtime.Interfaces.InMemoryAgentNavigationIntentService", LogLevel.Information);
builder.Logging.AddFilter("AgentBlazor", LogLevel.Information);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton<DojoWorkspaceService>();
builder.Services.AddSingleton<DemoAgentCustomizationStore>();
builder.Services.AddScoped<DemoFileWorkflowService>();
builder.Services.AddScoped<DojoRecipeReleaseWorkflowService>();
builder.Services.AddScoped<IncidentEscalationWorkflowService>();
builder.Services.AddScoped<SupplierComplianceWorkflowService>();
builder.Services.AddScoped<SupportInboxWorkflowService>();
builder.Services.AddScoped<ResponseOrchestrationWorkflowService>();
builder.Services.AddScoped<ReleaseDossierWorkflowService>();
builder.Services.Configure<DemoSecurityOptions>(builder.Configuration.GetSection(DemoSecurityOptions.SectionName));
builder.Services.Configure<DemoLoggingOptions>(builder.Configuration.GetSection(DemoLoggingOptions.SectionName));
builder.Services.Configure<DemoRemoteStorageOptions>(builder.Configuration.GetSection(DemoRemoteStorageOptions.SectionName));
builder.Services.Configure<DemoConversationOptions>(builder.Configuration.GetSection(DemoConversationOptions.SectionName));
builder.Services.Configure<DemoWorkflowOptions>(builder.Configuration.GetSection(DemoWorkflowOptions.SectionName));
// ---------------------------------------------------------------
// SQLite database convention — all Demo SQLite files live in
//   demo/AgentBlazor.Demo/data/
// The directory is created on startup if missing and is gitignored.
// ---------------------------------------------------------------
var demoDataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(demoDataDir);

var demoConversationOptions = builder.Configuration
    .GetSection(DemoConversationOptions.SectionName)
    .Get<DemoConversationOptions>()
    ?? new DemoConversationOptions();
if (string.IsNullOrWhiteSpace(demoConversationOptions.FilePath))
{
    demoConversationOptions.FilePath = Path.Combine(demoDataDir, "agentblazor-demo-conversations.json");
}
if (string.IsNullOrWhiteSpace(demoConversationOptions.ConnectionString))
{
    demoConversationOptions.ConnectionString =
        $"Data Source={Path.Combine(demoDataDir, "agentblazor-demo-conversations.db")}";
}

// EF Core conversation store (DemoConversation:Store=EFCore) — a custom
// IConversationStore implementation demonstrating the production-database pattern.
// SQLite-backed, durable, uses an IDbContextFactory so the singleton store never
// captures a scoped context.
if (string.Equals(demoConversationOptions.Store, "EFCore", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContextFactory<DemoConversationDbContext>(options =>
        options.UseSqlite(demoConversationOptions.ConnectionString));
    builder.Services.AddSingleton<DemoConversationDatabaseInitializer>();
    builder.Services.Configure<ConversationOptions>(conversationOptions =>
    {
        conversationOptions.MaxTurnsPerSession = demoConversationOptions.MaxTurnsPerSession;
        conversationOptions.SessionTimeout = demoConversationOptions.SessionTimeout;
    });
}
builder.Services.AddHttpClient("demo-remote-storage");
builder.Services.AddSingleton<IDemoRemoteStorageAdapter, DemoRemoteStorageAdapter>();
builder.Services.AddSingleton<IDemoChatRequestLog, JsonlDemoChatRequestLog>();
builder.Services.AddSingleton<IDemoTrafficLog, JsonlDemoTrafficLog>();
builder.Services.AddSingleton<DemoSessionBrowserService>();
builder.Services.AddSingleton<AgentBlazor.Core.Paid.IAdaptiveSuggestionService, DemoSuggestionService>();

var demoSecurityOptions = builder.Configuration.GetSection(DemoSecurityOptions.SectionName).Get<DemoSecurityOptions>()
    ?? new DemoSecurityOptions();
var proLicenseKey = builder.Configuration["AgentBlazor:LicenseKey"]
    ?? Environment.GetEnvironmentVariable("AGENTBLAZOR_LICENSE_KEY");
var proDataDirectory = builder.Configuration["AgentBlazor:DataDirectory"]
    ?? Environment.GetEnvironmentVariable("AGENTBLAZOR_DATA_DIRECTORY");

var openAiModel = FirstConfigured(builder.Configuration["OpenAI:Model"], "gpt-4o-mini")!;
var openAiApiKey = FirstConfigured(
    Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
    Environment.GetEnvironmentVariable("OpenAI__ApiKey"),
    builder.Configuration["OpenAI:ApiKey"]);
var ollamaModel = FirstConfigured(
    Environment.GetEnvironmentVariable("OLLAMA_MODEL"),
    Environment.GetEnvironmentVariable("Ollama__Model"),
    builder.Configuration["Ollama:Model"]);
var ollamaEndpoint = FirstConfigured(
    Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT"),
    Environment.GetEnvironmentVariable("Ollama__Endpoint"),
    builder.Configuration["Ollama:Endpoint"],
    "http://127.0.0.1:11434/v1")!;
var ollamaApiKey = FirstConfigured(
    Environment.GetEnvironmentVariable("OLLAMA_API_KEY"),
    Environment.GetEnvironmentVariable("Ollama__ApiKey"),
    builder.Configuration["Ollama:ApiKey"]);
var demoWorkflowOptions = builder.Configuration
    .GetSection(DemoWorkflowOptions.SectionName)
    .Get<DemoWorkflowOptions>()
    ?? new DemoWorkflowOptions();
if (string.IsNullOrWhiteSpace(demoWorkflowOptions.ConnectionString))
{
    demoWorkflowOptions.ConnectionString = $"Data Source={Path.Combine(demoDataDir, "agentblazor-demo-workflow.db")}";
}
var sharedAgentInstructionsPath = Path.Combine(builder.Environment.ContentRootPath, "agent-instructions.txt");
var sharedAgentInstructions = File.Exists(sharedAgentInstructionsPath)
    ? File.ReadAllText(sharedAgentInstructionsPath)
    : null;

var hasOpenAiProvider = !string.IsNullOrWhiteSpace(openAiApiKey);
var hasOllamaProvider = !string.IsNullOrWhiteSpace(ollamaModel);

if (!builder.Environment.IsDevelopment() &&
    demoSecurityOptions.RequireProviderInProduction &&
    !hasOpenAiProvider &&
    !(demoSecurityOptions.AllowOllamaInProduction && hasOllamaProvider))
{
    throw new InvalidOperationException(
        "The live demo requires a configured provider. Set OPENAI_API_KEY (recommended) or explicitly enable an Ollama production fallback.");
}

builder.Services.PostConfigure<DemoRemoteStorageOptions>(options =>
{
    options.HttpBaseUrl = FirstConfigured(
        Environment.GetEnvironmentVariable("DEMO_REMOTE_STORAGE_HTTP_BASE_URL"),
        options.HttpBaseUrl);
    options.HttpApiKey = FirstConfigured(
        Environment.GetEnvironmentVariable("DEMO_REMOTE_STORAGE_HTTP_API_KEY"),
        options.HttpApiKey);
    options.HttpBearerToken = FirstConfigured(
        Environment.GetEnvironmentVariable("DEMO_REMOTE_STORAGE_HTTP_BEARER_TOKEN"),
        options.HttpBearerToken);
});

builder.Services.PostConfigure<DemoLoggingOptions>(options =>
{
    options.DirectoryPath = FirstConfigured(
            Environment.GetEnvironmentVariable("DEMO_LOG_DIRECTORY"),
            Environment.GetEnvironmentVariable("DemoLogging__DirectoryPath"),
            options.DirectoryPath)
        ?? Path.Combine(Path.GetTempPath(), "agentblazor-demo-logs");
    options.AccessToken = FirstConfigured(
        Environment.GetEnvironmentVariable("DEMO_LOG_ACCESS_TOKEN"),
        Environment.GetEnvironmentVariable("DemoLogging__AccessToken"),
        options.AccessToken);
});

if (demoSecurityOptions.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"error":"Rate limit exceeded. Try again in a moment."}""",
            token);
    };

    options.AddPolicy(DemoSecurityOptions.AgentEndpointRateLimitPolicyName, httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, demoSecurityOptions.RateLimiting.PermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, demoSecurityOptions.RateLimiting.WindowSeconds)),
            QueueLimit = Math.Max(0, demoSecurityOptions.RateLimiting.QueueLimit),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });
});

builder.Services.AddDbContextFactory<DemoWorkflowDbContext>(options =>
    options.UseSqlite(demoWorkflowOptions.ConnectionString));
builder.Services.AddSingleton<DemoWorkflowDatabaseSeeder>();

// -----------------------------------------------------------------------------
// Agent Builder — database-backed IAgentRegistry (replace path).
// A custom IAgentRegistry is registered BEFORE AddAgentBlazor so it wins over the
// built-in InMemoryAgentRegistry snapshot, making this SQLite store the single
// source of truth for all agents. See the "Dynamic Agent Registration" section of
// the ab-agent-registration skill.
// -----------------------------------------------------------------------------
var agentDbConnectionString = $"Data Source={Path.Combine(demoDataDir, "agent-definitions.db")}";
builder.Services.AddDbContextFactory<DemoAgentDbContext>(options =>
    options.UseSqlite(agentDbConnectionString));
// Register the concrete registry first (so it can be resolved by the page + customizer),
// then as IAgentRegistry BEFORE AddAgentBlazor so it replaces the in-memory default.
builder.Services.AddSingleton<DatabaseBackedAgentRegistry>();
builder.Services.AddSingleton<AgentBlazor.Agents.IAgentRegistry>(sp =>
    sp.GetRequiredService<DatabaseBackedAgentRegistry>());
// The seeder reads the same shared-instructions file the static agents used to consume,
// so DB-seeded agents carry identical guidance.
builder.Services.AddSingleton(sp => new DemoAgentDatabaseSeeder(
    sp.GetRequiredService<IDbContextFactory<DemoAgentDbContext>>(),
    sp.GetRequiredService<DatabaseBackedAgentRegistry>(),
    sharedAgentInstructions));

builder.Services.AddAgentBlazor(options =>
{
    if (!string.IsNullOrWhiteSpace(openAiApiKey))
    {
        options.UseOpenAI(openAiApiKey, openAiModel);
    }
    else if (!string.IsNullOrWhiteSpace(ollamaModel))
    {
        options.UseOllama(ollamaModel, ollamaEndpoint, ollamaApiKey);
    }

    if (builder.Environment.IsDevelopment())
    {
        options.UseDevTools();
    }

    if (!string.IsNullOrWhiteSpace(proLicenseKey))
    {
        options.UseProLicense(proLicenseKey, proDataDirectory);
    }

    options.UseMiddleware<DemoChatRequestLoggingMiddleware>();

    // Pin provider-level chat options (e.g. reasoning effort for gpt-5.6 models).
    options.ConfigureChatOptions(o =>
    {
        // Demo uses gpt-4o-mini by default — this is an API showcase.
        // For gpt-5.6, pin: o.ProviderOptions["reasoning_effort"] = "low";
    });

    // Register demo service tools available to all agents.
    options.AddTool(
        "lookup-glossary",
        "Look up a term in the AgentBlazor glossary and return its definition.",
        [new AgentToolParameter("term", "The glossary term to look up.")],
        async (args, sp, ct) =>
        {
            var term = args.TryGetValue("term", out var t) ? t?.ToString() ?? "" : "";
            var glossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent"] = "An AI-powered component that can reason, use tools, and take actions within a Blazor app.",
                ["capability"] = "A logical grouping of related agent actions, annotated with [AgentCapability].",
                ["action"] = "A single executable unit of agent work, annotated with [AgentAction].",
                ["handoff"] = "Transfer of conversation control from one agent to another.",
                ["workflow"] = "A multi-step agent process that guides users through a business scenario.",
            };
            return glossary.TryGetValue(term, out var def) ? def : $"Term '{term}' not found. Available: {string.Join(", ", glossary.Keys)}.";
        });

    options.AddTool(
        "current-time",
        "Return the current UTC date and time.",
        [],
        (args, sp, ct) => Task.FromResult(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")));

    options.ConfigureBuilder(abBuilder =>
    {
        abBuilder.EnablePromptTracing();

        // Conversation persistence: the Demo demonstrates the incremental persistence model 
        // (append once, targeted UpdateTurnAsync patches for enriched/edited turns, never full-history rewrites) 
        // across three store backends:
        //   - JsonFile (default) — durable JSON-file store
        //   - EFCore           — durable SQLite EF Core store (custom IConversationStore)
        //   - InMemory         — ephemeral
        if (string.Equals(demoConversationOptions.Store, "JsonFile", StringComparison.OrdinalIgnoreCase))
        {
            abBuilder.UseJsonFileConversationStore(
                demoConversationOptions.FilePath,
                configure: conversationOptions =>
                {
                    conversationOptions.MaxTurnsPerSession = demoConversationOptions.MaxTurnsPerSession;
                    conversationOptions.SessionTimeout = demoConversationOptions.SessionTimeout;
                });
        }
        else if (string.Equals(demoConversationOptions.Store, "EFCore", StringComparison.OrdinalIgnoreCase))
        {
            abBuilder.UseConversationStore(sp => new DemoConversationStore(
                sp.GetRequiredService<IDbContextFactory<DemoConversationDbContext>>(),
                sp.GetService<IOptions<ConversationOptions>>()));
        }

        // ---------------------------------------------------------------------
        // Agents are defined in the database (see DemoAgentDatabaseSeeder) and
        // resolved at runtime through DatabaseBackedAgentRegistry — the "replace"
        // dynamic-registration path. This builder block only registers things the
        // DB store does NOT own: capability classes (for [AgentAction] discovery),
        // data schemas, the runtime customizer, prompt tracing, and conversation
        // stores. There are deliberately no AddAgent/AddWorkflow calls here.
        // ---------------------------------------------------------------------
        abBuilder.AddDataSchema(new AgentDataSchemaSet
        {
            Name = "support-data",
            Description = "Read-safe support ticket fields used by the support inbox workflow. This is planning context only; ticket reads and drafts still go through typed workflow actions.",
            Entities =
            [
                new AgentEntitySchema
                {
                    Name = "support_tickets",
                    Description = "Support tickets visible in the demo queue.",
                    ClrTypeName = typeof(SupportTicketRow).FullName,
                    Properties =
                    [
                        new AgentEntityPropertySchema { Name = "Id", Type = "string", IsKey = true, Description = "Ticket identifier such as TCK-1042." },
                        new AgentEntityPropertySchema { Name = "Subject", Type = "string", Description = "Customer-visible ticket subject." },
                        new AgentEntityPropertySchema { Name = "Team", Type = "string", Description = "Owning support team." },
                        new AgentEntityPropertySchema { Name = "Priority", Type = "string", Description = "Priority label such as High or Medium." },
                        new AgentEntityPropertySchema { Name = "AgeDays", Type = "integer", Description = "Age of the ticket in days." },
                        new AgentEntityPropertySchema { Name = "WaitingOnReply", Type = "boolean", Description = "Whether the customer is waiting on a support reply." },
                        new AgentEntityPropertySchema { Name = "EscalationRisk", Type = "boolean", Description = "Whether the ticket is at risk of escalation." },
                        new AgentEntityPropertySchema { Name = "MissingEvidence", Type = "boolean", Description = "Whether draft preparation is blocked by missing evidence." }
                    ]
                }
            ]
        });

        // Capability classes — the [AgentAction] methods must be discoverable by the
        // ReflectionAgentCapabilityRegistry even though the agent definitions themselves
        // live in the database. AddWorkflow<T>(name, ...) would ALSO create an agent
        // registration (dead, since the registry is DB-backed); AddCapability<T>() is
        // the exact subset needed here.
        abBuilder.AddCapability<SupplierComplianceCapabilities>();
        abBuilder.AddCapability<SupportInboxCapabilities>();
        abBuilder.AddCapability<DemoFileWorkflowCapabilities>();
        abBuilder.AddCapability<DojoRecipeReleaseCapabilities>();
        abBuilder.AddCapability<IncidentEscalationCapabilities>();
        abBuilder.AddCapability<ResponseOrchestrationCapabilities>();
        abBuilder.AddCapability<ReleaseDossierCapabilities>();
        abBuilder.AddCapability<RuntimeProbeCapabilities>();
        abBuilder.AddCapability<CustomizationDemoCapabilities>();

        // Runtime customization showcase: persona + tool set edited live on
        // /demo/customization via the IAgentRuntimeCustomizer seam.
        abBuilder.AddRuntimeCustomizer<DemoAgentCustomizer>();
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DemoWorkflowDatabaseSeeder>();
    await seeder.InitializeAsync(CancellationToken.None);

    var agentSeeder = scope.ServiceProvider.GetRequiredService<DemoAgentDatabaseSeeder>();
    await agentSeeder.InitializeAsync(CancellationToken.None);

    if (string.Equals(demoConversationOptions.Store, "EFCore", StringComparison.OrdinalIgnoreCase))
    {
        var conversationInitializer = scope.ServiceProvider
            .GetRequiredService<DemoConversationDatabaseInitializer>();
        await conversationInitializer.InitializeAsync(CancellationToken.None);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (demoSecurityOptions.TrustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<DemoTrafficLoggingMiddleware>();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
var agentEndpoints = app.MapAgentBlazorEndpoints();
app.MapDemoLogEndpoints();

if (demoSecurityOptions.RateLimiting.Enabled)
{
    agentEndpoints.RequireRateLimiting(DemoSecurityOptions.AgentEndpointRateLimitPolicyName);
}

app.Run();

static string? FirstConfigured(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return null;
}
