using AgentBlazor;
using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Demo.Components;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.ServiceDefaults;
using AgentBlazor.Demo.Services;
using AgentBlazor.Core.Data;
using AgentBlazor.Core.Runtime.Tools;
using AgentBlazor.Core.Persistence;
using AgentBlazor.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.AddServiceDefaults();

// Ensure [AgentFlow] logs are visible when running prompts
builder.Logging.AddFilter("AgentBlazor.Core.Runtime.Agents.AgentRuntime", LogLevel.Information);
builder.Logging.AddFilter("AgentBlazor.Core.Runtime.Interfaces.InMemoryAgentNavigationIntentService", LogLevel.Information);
builder.Logging.AddFilter("AgentBlazor", LogLevel.Information);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton<DojoWorkspaceService>();
builder.Services.AddScoped<DemoFileWorkflowService>();
builder.Services.AddScoped<DojoRecipeReleaseWorkflowService>();
builder.Services.AddScoped<IncidentEscalationWorkflowService>();
builder.Services.AddScoped<SupplierComplianceWorkflowService>();
builder.Services.AddScoped<SupportInboxWorkflowService>();
builder.Services.AddScoped<ResponseOrchestrationWorkflowService>();
builder.Services.AddScoped<ReleaseDossierWorkflowService>();
builder.Services.Configure<DemoSecurityOptions>(builder.Configuration.GetSection(DemoSecurityOptions.SectionName));
builder.Services.Configure<DemoLoggingOptions>(builder.Configuration.GetSection(DemoLoggingOptions.SectionName));
builder.Services.Configure<DemoTokenPricingOptions>(builder.Configuration.GetSection(DemoTokenPricingOptions.SectionName));
builder.Services.Configure<DemoRemoteStorageOptions>(builder.Configuration.GetSection(DemoRemoteStorageOptions.SectionName));
builder.Services.Configure<DemoConversationOptions>(builder.Configuration.GetSection(DemoConversationOptions.SectionName));
builder.Services.Configure<DemoWorkflowOptions>(builder.Configuration.GetSection(DemoWorkflowOptions.SectionName));
var demoConversationOptions = builder.Configuration
    .GetSection(DemoConversationOptions.SectionName)
    .Get<DemoConversationOptions>()
    ?? new DemoConversationOptions();

// Per-session conversation-turn rollups (usage + execution plans) for the session
// browser. The Null query is the default (JsonFile/InMemory stores have no DB columns);
// the EFCore branch below replaces it with the SQL Server-backed batch query.
builder.Services.AddSingleton<IDemoConversationTurnQuery, NullDemoConversationTurnQuery>();

// Unified EF Core DbContext — conversation sessions/turns + agent definitions in one
// SQL Server database, managed by code-first migrations.
// Connection string is injected by Aspire AppHost as "ConnectionStrings:demo-db".
var demoConnectionString = builder.Configuration.GetConnectionString("demo-db")
    ?? throw new InvalidOperationException(
        "Connection string 'demo-db' not found. When running via Aspire AppHost, this is injected automatically. " +
        "For standalone development, add 'ConnectionStrings:demo-db' to appsettings.json.");
builder.Services.AddDbContextFactory<DemoDbContext>(options =>
    options.UseSqlServer(demoConnectionString));

// EF Core conversation store (DemoConversation:Store=EFCore) — a custom
// IConversationStore implementation demonstrating the production-database pattern.
if (string.Equals(demoConversationOptions.Store, "EFCore", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IDemoConversationTurnQuery, DemoConversationTurnQuery>();
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
// Single source of truth for token pricing — shared by the JSONL request log and the
// EF Core conversation store so both agree on what a turn cost.
builder.Services.AddSingleton<DemoUsageCostCalculator>();
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
    options.UseSqlServer(demoConnectionString));
builder.Services.AddSingleton<DemoWorkflowDatabaseSeeder>();

// -----------------------------------------------------------------------------
// Agent Builder — database-backed IAgentRegistry (replace path).
// A custom IAgentRegistry is registered BEFORE AddAgentBlazor so it wins over the
// built-in InMemoryAgentRegistry snapshot, making this SQL Server store the single
// source of truth for all agents. See the "Dynamic Agent Registration" section of
// the ab-agent-registration skill.
// -----------------------------------------------------------------------------
// Agent definitions share the unified DemoDbContext — no separate connection string.
// Register the concrete registry first (so it can be resolved by the page + customizer),
// then as IAgentRegistry BEFORE AddAgentBlazor so it replaces the in-memory default.
builder.Services.AddSingleton<DatabaseBackedAgentRegistry>();
builder.Services.AddSingleton<AgentBlazor.Agents.IAgentRegistry>(sp =>
    sp.GetRequiredService<DatabaseBackedAgentRegistry>());
// Alias the async seam onto the SAME instance. AddAgentBlazor's own registration cannot
// cover this: it resolves IAgentRegistry, which this line has already replaced. Without
// this alias the render path would keep resolving the thread-pool shim and the database
// read would still block the renderer -- the very deadlock this store must avoid.
// Both interfaces must stay on one instance or the render path reads a stale cache.
builder.Services.AddSingleton<AgentBlazor.Agents.IAsyncAgentRegistry>(sp =>
    sp.GetRequiredService<DatabaseBackedAgentRegistry>());

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
        //   - EFCore           — durable SQL Server EF Core store (custom IConversationStore)
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
                sp.GetRequiredService<IDbContextFactory<DemoDbContext>>(),
                sp.GetRequiredService<DemoUsageCostCalculator>(),
                sp.GetService<IOptions<ConversationOptions>>()));
        }

        // ---------------------------------------------------------------------
        // Agents are defined in the database (seeded at startup) and
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
        abBuilder.AddCapability<DemoAssemblyCapabilities>();

        abBuilder.AddRuntimeCustomizer<DemoAgentCustomizer>();
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    // Apply EF Core migrations for DemoDbContext (conversations, agent definitions).
    var dbFactory = scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<DemoDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
    await db.Database.MigrateAsync(CancellationToken.None);

    // Apply EF Core migrations for DemoWorkflowDbContext (workflow tables).
    var workflowDbFactory = scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<DemoWorkflowDbContext>>();
    await using var workflowDb = await workflowDbFactory.CreateDbContextAsync(CancellationToken.None);
    await workflowDb.Database.MigrateAsync(CancellationToken.None);

    // Run the workflow seeder (adds columns that may not be in migrations yet,
    // e.g. BudgetFriendly, OnePotMeal, Vegan on dojo_workspaces).
    var seeder = scope.ServiceProvider.GetRequiredService<DemoWorkflowDatabaseSeeder>();
    await seeder.InitializeAsync(CancellationToken.None);

    // Seed baseline agent definitions (idempotent — existing agents are preserved).
    await using var agentScope = await dbFactory.CreateDbContextAsync(CancellationToken.None);
    await SeedAgentDefinitionsAsync(agentScope, sharedAgentInstructions, CancellationToken.None);

    // Warm the registry cache now that migrations + agent seeding have run, so no later
    // read has to touch the database from a render thread. Must come after the seeding
    // above, or the baseline agents would be missing from the cache until the next refresh.
    await scope.ServiceProvider
        .GetRequiredService<DatabaseBackedAgentRegistry>()
        .InitializeAsync(CancellationToken.None);
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
app.MapDefaultEndpoints();

if (demoSecurityOptions.RateLimiting.Enabled)
{
    agentEndpoints.RequireRateLimiting(DemoSecurityOptions.AgentEndpointRateLimitPolicyName);
}

app.Run();

static async Task SeedAgentDefinitionsAsync(
    DemoDbContext db,
    string? sharedInstructions,
    CancellationToken cancellationToken)
{
    var shared = sharedInstructions ??
        "You are a helpful demo assistant. Keep replies concise and friendly.";

    foreach (var seed in BuildSeeds(shared))
    {
        var existing = db.AgentDefinitions.AsNoTracking()
            .FirstOrDefault(e => e.Name.ToLower() == seed.Name.ToLower());
        if (existing is not null)
        {
            continue; // already seeded (idempotent — user edits persist).
        }

        db.AgentDefinitions.Add(new DemoAgentDefinitionEntity
        {
            Id = Guid.NewGuid(),
            Name = seed.Name,
            Description = seed.Description,
            Instructions = seed.Instructions,
            AllowedComponentsJson = AgentDefinitionEntity.SerializeSet(seed.AllowedComponents),
            AllowedActionsJson = AgentDefinitionEntity.SerializeSet(seed.AllowedActions),
            // Workflow agents: AllowedCapabilityActions has its own column —
            // persist it there (mirrors the runtime model).
            AllowedCapabilityActionsJson = AgentDefinitionEntity.SerializeSet(seed.AllowedCapabilityActions),
            AllowedDataSchemasJson = AgentDefinitionEntity.SerializeSet(seed.AllowedDataSchemas),
            MetadataJson = AgentDefinitionEntity.SerializeDictionary(seed.Metadata),
        });
    }

    await db.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// Baseline agents mirroring the static <c>AddAgent</c>/<c>AddWorkflow</c> declarations
/// previously in <c>Program.cs</c>.
/// </summary>
static IEnumerable<AgentRegistration> BuildSeeds(string sharedInstructions)
{
    return
    [
        Agent(
            "Workflow Hub Agent",
            "Focused on routing users toward the right semantic workflow showcase and explaining the workflow-first product story.",
            instructions: sharedInstructions),
        Agent(
            "Supplier Analyst Agent",
            "Focused on the component reference surface for data-centric controls and selection patterns.",
            instructions: sharedInstructions,
            components: ["AgentDataGrid", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentSelect", "AgentAutocomplete"],
            routePrefixes: ["/demo/components", "/demo/components/datagrid", "/demo/components/select", "/demo/components/autocomplete", "/demo/components/date-picker", "/demo/components/date-range-picker", "/demo/components/tree-view"]),
        Agent(
            "Workflow Orchestrator Agent",
            "Focused on the component reference surface for form, dialog, command, and file workflow primitives.",
            instructions: sharedInstructions,
            components: ["AgentStepper", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentTreeView", "AgentCommandBar", "AgentFileUpload"],
            routePrefixes: ["/demo/components", "/demo/components/form", "/demo/components/dialog", "/demo/components/tabs", "/demo/components/stepper", "/demo/components/command-bar", "/demo/components/file-upload"]),
        Workflow<SupplierComplianceWorkflowService>(
            "Supplier Compliance Agent",
            "Focused on supplier risk review, explanation, recovery-playbook guidance, and remediation preparation.",
            instructions: sharedInstructions,
            components: ["AgentDataGrid", "AgentDialog"],
            routePrefixes: ["/demo/workflows/supplier-compliance"]),
        Workflow<SupportInboxWorkflowService>(
            "Support Inbox Agent",
            "Focused on support tickets that need a reply, reply drafting, escalation, and queue guidance.",
            instructions: sharedInstructions,
            components: ["AgentDataGrid", "AgentDialog"],
            dataSchemas: ["support-data"],
            routePrefixes: ["/demo/workflows/support-inbox"]),
        Workflow<DemoFileWorkflowCapabilities>(
            "File Workflow Agent",
            "Focused on file audit bundles, remote handoff, and token verification workflows.",
            instructions: sharedInstructions,
            components: ["AgentFileUpload", "AgentCommandBar"],
            routePrefixes: ["/demo/workflows/file-audit-bundle"]),
        Workflow<DojoRecipeReleaseWorkflowService>(
            "Recipe Release Agent",
            "Focused on recipe readiness, release blockers, recovery-playbook guidance, and publish-ready draft preparation.",
            instructions: sharedInstructions,
            components: ["AgentForm", "AgentDataGrid", "AgentDialog"],
            routePrefixes: ["/demo/workflows/recipe-release"]),
        Workflow<IncidentEscalationWorkflowService>(
            "Incident Escalation Agent",
            "Focused on incident triage, evidence review, escalation brief preparation, and recovery from blocked review-board handoffs.",
            instructions: sharedInstructions,
            components: ["AgentTreeView", "AgentTabs", "AgentStepper", "AgentCommandBar", "AgentDialog"],
            routePrefixes: ["/demo/workflows/incident-escalation"]),
        Workflow<ResponseOrchestrationWorkflowService>(
            "Response Orchestration Agent",
            "Focused on cross-system orchestration across supplier risk, audit evidence, and incident escalation, including guided subsystem-stage advancement before operational handoff.",
            instructions: sharedInstructions,
            components: ["AgentDialog"],
            routePrefixes: ["/demo/workflows/response-orchestration"]),
        Workflow<ReleaseDossierWorkflowService>(
            "Release Dossier Agent",
            "Focused on recipe release readiness and audit evidence orchestration before release dossier handoff.",
            instructions: sharedInstructions,
            components: ["AgentDialog"],
            routePrefixes: ["/demo/workflows/release-dossier"]),
        Workflow<RuntimeProbeCapabilities>(
            "Runtime Probe Agent",
            "Focused on validating runtime cancellation behavior in the live demo host.",
            instructions: sharedInstructions,
            routePrefixes: ["/demo/workflows/runtime-probe"]),
    ];
}

static AgentRegistration Agent(
    string name,
    string description,
    string? instructions = null,
    string[]? components = null,
    string[]? routePrefixes = null,
    string[]? dataSchemas = null,
    IReadOnlyList<string>? capabilityTypes = null)
    => new()
    {
        Name = name,
        Description = description,
        Instructions = instructions,
        AllowedComponents = new HashSet<string>(components ?? [], StringComparer.OrdinalIgnoreCase),
        AllowedDataSchemas = new HashSet<string>(dataSchemas ?? [], StringComparer.OrdinalIgnoreCase),
        AllowedCapabilityActions = new HashSet<string>(capabilityTypes ?? [], StringComparer.OrdinalIgnoreCase),
        Metadata = BuildRouteMetadata(routePrefixes)
    };

static AgentRegistration Workflow<TCapability>(
    string name,
    string description,
    string? instructions = null,
    string[]? components = null,
    string[]? routePrefixes = null,
    string[]? dataSchemas = null)
    => Agent(
        name,
        description,
        instructions: instructions,
        components: components,
        routePrefixes: routePrefixes,
        dataSchemas: dataSchemas,
        capabilityTypes: GetCapabilityActionIds(typeof(TCapability)));

static Dictionary<string, string> BuildRouteMetadata(string[]? routePrefixes)
{
    var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (routePrefixes is { Length: > 0 })
    {
        metadata["route_prefixes"] = string.Join(",", routePrefixes);
    }
    return metadata;
}

static IReadOnlyList<string> GetCapabilityActionIds(Type capabilityType)
{
    var capabilityId = capabilityType.GetCustomAttribute<AgentCapabilityAttribute>()?.CapabilityId
        ?? ToCapabilityId(capabilityType.Name);

    return capabilityType
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(static m => m.GetCustomAttribute<AgentActionAttribute>() is not null)
        .Select(m =>
        {
            var actionId = m.GetCustomAttribute<AgentActionAttribute>()!.ActionId ?? ToSnakeCase(m.Name);
            return $"{capabilityId}.{actionId}";
        })
        .ToArray();
}

static string ToCapabilityId(string typeName)
    => ToSnakeCase(typeName.EndsWith("Capabilities", StringComparison.Ordinal)
        ? typeName[..^"Capabilities".Length]
        : typeName);

static string ToSnakeCase(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    var builder = new StringBuilder(value.Length + 8);
    for (var i = 0; i < value.Length; i++)
    {
        var current = value[i];
        if (char.IsUpper(current))
        {
            if (i > 0)
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(current));
        }
        else
        {
            builder.Append(current);
        }
    }
    return builder.ToString();
}

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
