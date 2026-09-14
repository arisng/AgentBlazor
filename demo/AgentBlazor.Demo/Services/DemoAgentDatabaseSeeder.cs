using System.Reflection;
using System.Text;
using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Demo.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Creates the SQLite schema for <see cref="DemoAgentDbContext"/> at startup and seeds
/// the baseline agent definitions so the replaced, database-backed
/// <see cref="IAgentRegistry"/> starts with the same agents the Demo previously declared
/// statically via <c>AddAgent</c>/<c>AddWorkflow</c>.
/// </summary>
/// <remarks>
/// Workflow agents carry <see cref="AgentRegistration.AllowedCapabilityActions"/>
/// (e.g. <c>"supplier_compliance.show_at_risk_suppliers"</c>). Because
/// <c>AgentCapabilityConventions</c> is internal to the library, this seeder derives the
/// same ids from the public <c>[AgentCapability]</c>/<c>[AgentAction]</c> attributes.
/// Capability discovery, data schemas, service tools, and the runtime customizer remain
/// registered in <c>Program.cs</c> independently of the registry.
/// </remarks>
internal sealed class DemoAgentDatabaseSeeder(
    IDbContextFactory<DemoAgentDbContext> dbContextFactory,
    DatabaseBackedAgentRegistry registry,
    string? sharedInstructions)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        foreach (var seed in BuildSeeds(sharedInstructions))
        {
            var existing = db.AgentDefinitions.AsNoTracking()
                .FirstOrDefault(e => e.Name.ToLower() == seed.Name.ToLower());
            if (existing is not null)
            {
                continue; // already seeded (idempotent — user edits persist).
            }

            db.AgentDefinitions.Add(new AgentDefinitionEntity
            {
                Id = Guid.NewGuid(),
                Name = seed.Name,
                Description = seed.Description,
                Instructions = seed.Instructions,
                AllowedComponentsJson = AgentDefinitionEntity.SerializeSet(seed.AllowedComponents),
                AllowedActionsJson = AgentDefinitionEntity.SerializeSet(seed.AllowedActions),
                AllowedDataSchemasJson = AgentDefinitionEntity.SerializeSet(seed.AllowedDataSchemas),
                MetadataJson = AgentDefinitionEntity.SerializeDictionary(seed.Metadata),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        registry.RefreshFromDatabase();
    }

    /// <summary>
    /// Baseline agents mirroring the static <c>AddAgent</c>/<c>AddWorkflow</c> declarations
    /// previously in <c>Program.cs</c>. <paramref name="sharedInstructions"/> is the content of
    /// <c>agent-instructions.txt</c> (may be <see langword="null"/> if the file is absent).
    /// </summary>
    private static IEnumerable<AgentRegistration> BuildSeeds(string? sharedInstructions)
    {
        var shared = sharedInstructions; // content of agent-instructions.txt, or null
        var sharedInstructionsOrDefault = shared ??
            "You are a helpful demo assistant. Keep replies concise and friendly.";
        return
        [
            Agent(
                "Workflow Hub Agent",
                "Focused on routing users toward the right semantic workflow showcase and explaining the workflow-first product story.",
                instructions: sharedInstructionsOrDefault),
            Agent(
                "Supplier Analyst Agent",
                "Focused on the component reference surface for data-centric controls and selection patterns.",
                instructions: sharedInstructionsOrDefault,
                components: ["AgentDataGrid", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentSelect", "AgentAutocomplete"],
                routePrefixes: ["/demo/components", "/demo/components/datagrid", "/demo/components/select", "/demo/components/autocomplete", "/demo/components/date-picker", "/demo/components/date-range-picker", "/demo/components/tree-view"]),
            Agent(
                "Workflow Orchestrator Agent",
                "Focused on the component reference surface for form, dialog, command, and file workflow primitives.",
                instructions: sharedInstructionsOrDefault,
                components: ["AgentStepper", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentTreeView", "AgentCommandBar", "AgentFileUpload"],
                routePrefixes: ["/demo/components", "/demo/components/form", "/demo/components/dialog", "/demo/components/tabs", "/demo/components/stepper", "/demo/components/command-bar", "/demo/components/file-upload"]),
            Workflow<SupplierComplianceWorkflowService>(
                "Supplier Compliance Agent",
                "Focused on supplier risk review, explanation, recovery-playbook guidance, and remediation preparation.",
                instructions: sharedInstructionsOrDefault,
                components: ["AgentDataGrid", "AgentDialog"],
                routePrefixes: ["/demo/workflows/supplier-compliance"]),
            Workflow<SupportInboxWorkflowService>(
                "Support Inbox Agent",
                "Focused on support tickets that need a reply, reply drafting, escalation, and queue guidance.",
                instructions: sharedInstructionsOrDefault,
                components: ["AgentDataGrid", "AgentDialog"],
                dataSchemas: ["support-data"],
                routePrefixes: ["/demo/workflows/support-inbox"]),
            Workflow<DemoFileWorkflowCapabilities>(
                "File Workflow Agent",
                "Focused on file audit bundles, remote handoff, and token verification workflows.",
                instructions: sharedInstructionsOrDefault,
                components: ["AgentFileUpload", "AgentCommandBar"],
                routePrefixes: ["/demo/workflows/file-audit-bundle"]),
            Workflow<DojoRecipeReleaseWorkflowService>(
                "Recipe Release Agent",
                "Focused on recipe readiness, release blockers, recovery-playbook guidance, and publish-ready draft preparation.",
                instructions: sharedInstructionsOrDefault,
                components: ["AgentForm", "AgentDataGrid", "AgentDialog"],
                routePrefixes: ["/demo/workflows/recipe-release"]),
            Workflow<IncidentEscalationWorkflowService>(
                "Incident Escalation Agent",
                "Focused on incident triage, evidence review, escalation brief preparation, and recovery from blocked review-board handoffs.",
                instructions: sharedInstructionsOrDefault,
                components: ["AgentTreeView", "AgentTabs", "AgentStepper", "AgentCommandBar", "AgentDialog"],
                routePrefixes: ["/demo/workflows/incident-escalation"]),
            Workflow<ResponseOrchestrationWorkflowService>(
                "Response Orchestration Agent",
                "Focused on cross-system orchestration across supplier risk, audit evidence, and incident escalation, including guided subsystem-stage advancement before operational handoff.",
                instructions: sharedInstructionsOrDefault,
                components: ["AgentDialog"],
                routePrefixes: ["/demo/workflows/response-orchestration"]),
            Workflow<ReleaseDossierWorkflowService>(
                "Release Dossier Agent",
                "Focused on recipe release readiness and audit evidence orchestration before release dossier handoff.",
                instructions: sharedInstructionsOrDefault,
                components: ["AgentDialog"],
                routePrefixes: ["/demo/workflows/release-dossier"]),
            Workflow<RuntimeProbeCapabilities>(
                "Runtime Probe Agent",
                "Focused on validating runtime cancellation behavior in the live demo host.",
                instructions: sharedInstructionsOrDefault,
                routePrefixes: ["/demo/workflows/runtime-probe"]),
            Workflow<CustomizationDemoCapabilities>(
                "Customization Demo Agent",
                "Focused on demonstrating per-agent runtime customization: edit the persona and toggle the tool set on the customization showcase, then chat with the customized agent.",
                instructions: sharedInstructionsOrDefault,
                routePrefixes: ["/demo/customization"]),
        ];
    }

    private static AgentRegistration Agent(
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

    private static AgentRegistration Workflow<TCapability>(
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

    private static Dictionary<string, string> BuildRouteMetadata(string[]? routePrefixes)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (routePrefixes is { Length: > 0 })
        {
            metadata["route_prefixes"] = string.Join(",", routePrefixes);
        }

        return metadata;
    }

    /// <summary>
    /// Derives <c>"capabilityId.localActionId"</c> ids from the public
    /// <c>[AgentCapability]</c>/<c>[AgentAction]</c> attributes, mirroring the internal
    /// <c>AgentCapabilityConventions</c> so the ids match what the runtime expects.
    /// </summary>
    private static IReadOnlyList<string> GetCapabilityActionIds(Type capabilityType)
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

    private static string ToCapabilityId(string typeName)
        => ToSnakeCase(typeName.EndsWith("Capabilities", StringComparison.Ordinal)
            ? typeName[..^"Capabilities".Length]
            : typeName);

    private static string ToSnakeCase(string value)
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
}