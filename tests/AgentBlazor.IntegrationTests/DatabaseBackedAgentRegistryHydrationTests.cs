using AgentBlazor.Agents;
using AgentBlazor.Core.Persistence;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// Regression suite for the persona hydration merge in
/// <see cref="DatabaseBackedAgentRegistry"/>: the user-managed persona (Metadata
/// <c>agent_builder.persona</c>) is merged into <c>AgentRegistration.Instructions</c> at
/// hydration, the cache always holds the merged form, and the round-trip is idempotent
/// (platform instructions are sourced from the entity column, never from the merged
/// registration, so the persona is never duplicated).
/// </summary>
public class DatabaseBackedAgentRegistryHydrationTests
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public DatabaseBackedAgentRegistryHydrationTests()
    {
        // One open connection keeps the in-memory SQLite database alive across the
        // registry's per-operation DbContexts.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<DemoDbContext>(
            options => options.UseSqlite(_connection));
        services.AddSingleton<DatabaseBackedAgentRegistry>();
        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<DatabaseBackedAgentRegistry> CreateRegistryAsync()
    {
        await using var db = await _provider
            .GetRequiredService<IDbContextFactory<DemoDbContext>>()
            .CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        var registry = _provider.GetRequiredService<DatabaseBackedAgentRegistry>();
        await registry.InitializeAsync();
        return registry;
    }

    private static AgentRegistration BuildRegistration(
        string name,
        string? platform,
        string? persona,
        params string[] enabledToolIds)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(persona))
        {
            metadata[DatabaseBackedAgentRegistry.PersonaKey] = persona;
        }

        if (enabledToolIds.Length > 0)
        {
            metadata[DatabaseBackedAgentRegistry.EnabledToolsKey] = string.Join(",", enabledToolIds);
        }

        return new AgentRegistration
        {
            Name = name,
            Description = "test",
            Instructions = platform,
            AllowedComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedCapabilityActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedDataSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Metadata = metadata
        };
    }

    [Fact]
    public async Task Hydration_MergesPersonaAfterPlatformInstructions()
    {
        var registry = await CreateRegistryAsync();

        await registry.AddOrUpdateAsync(BuildRegistration(
            "support-agent", platform: "PLATFORM TEXT", persona: "USER PERSONA"));

        var agent = Assert.Single(registry.GetAll());
        Assert.Equal("PLATFORM TEXT\n\nUSER PERSONA", agent.Instructions);
        // Non-destructive: the metadata key survives hydration so direct readers keep working.
        Assert.Equal("USER PERSONA", agent.Metadata[DatabaseBackedAgentRegistry.PersonaKey]);
    }

    [Fact]
    public async Task Hydration_PersonaOnlyAgent_UsesPersonaVerbatim()
    {
        var registry = await CreateRegistryAsync();

        await registry.AddOrUpdateAsync(BuildRegistration(
            "persona-only", platform: null, persona: "ONLY PERSONA"));

        var agent = Assert.Single(registry.GetAll());
        Assert.Equal("ONLY PERSONA", agent.Instructions);
    }

    [Fact]
    public async Task Hydration_NoPersona_KeepsPlatformInstructions()
    {
        var registry = await CreateRegistryAsync();

        await registry.AddOrUpdateAsync(BuildRegistration(
            "platform-only", platform: "PLATFORM TEXT", persona: null));

        var agent = Assert.Single(registry.GetAll());
        Assert.Equal("PLATFORM TEXT", agent.Instructions);
    }

    [Fact]
    public async Task Cache_IsConsistent_ImmediatelyAfterSave()
    {
        var registry = await CreateRegistryAsync();

        await registry.AddOrUpdateAsync(BuildRegistration(
            "support-agent", platform: "PLATFORM", persona: "PERSONA"));

        // The cache must hold the MERGED registration right after the write — no restart
        // or RefreshFromDatabase required for the persona to reach the LLM system prompt.
        var agent = Assert.Single(registry.GetAll());
        Assert.Equal("PLATFORM\n\nPERSONA", agent.Instructions);
    }

    [Fact]
    public async Task RoundTrip_PlatformSourcedFromEntity_DoesNotDuplicatePersona()
    {
        var registry = await CreateRegistryAsync();

        // First save: platform + persona.
        await registry.AddOrUpdateAsync(BuildRegistration(
            "support-agent", platform: "PLATFORM", persona: "PERSONA"));

        // Builder edit/save round-trip: platform text is re-sourced from the ENTITY column
        // (GetPlatformInstructionsAsync), the persona from Metadata — never from the merged
        // registration.Instructions.
        var platform = await registry.GetPlatformInstructionsAsync("support-agent");
        var persona = registry.GetAll().Single().Metadata[DatabaseBackedAgentRegistry.PersonaKey];
        await registry.AddOrUpdateAsync(BuildRegistration(
            "support-agent", platform: platform, persona: persona));

        var agent = Assert.Single(registry.GetAll());
        Assert.Equal("PLATFORM\n\nPERSONA", agent.Instructions);
        Assert.Equal(1, CountOccurrences(agent.Instructions, "PERSONA"));
    }

    [Fact]
    public async Task TryGetRuntimeCustomization_ReturnsToolsOnly_NotPersona()
    {
        var registry = await CreateRegistryAsync();

        await registry.AddOrUpdateAsync(BuildRegistration(
            "support-agent",
            platform: "PLATFORM",
            persona: "PERSONA",
            "support_inbox.show_open_tickets"));

        var customization = registry.TryGetRuntimeCustomization("support-agent");
        Assert.NotNull(customization);
        Assert.Null(customization.UserContext);
        Assert.NotNull(customization.EnabledToolIds);
        Assert.Contains("support_inbox.show_open_tickets", customization.EnabledToolIds);

        // Persona is NOT part of the runtime customization — it lives in Instructions.
        Assert.Equal("PLATFORM\n\nPERSONA", registry.GetAll().Single().Instructions);
    }

    [Fact]
    public async Task SetRuntimeCustomization_OnExistingAgent_DoesNotDuplicatePersona()
    {
        var registry = await CreateRegistryAsync();

        await registry.AddOrUpdateAsync(BuildRegistration(
            "support-agent", platform: "PLATFORM", persona: "PERSONA"));

        // Customization-only update path re-sources platform from the entity column.
        registry.SetRuntimeCustomization("support-agent", "NEW PERSONA", new HashSet<string>(new[] { "tool_a" }, StringComparer.OrdinalIgnoreCase));

        var agent = Assert.Single(registry.GetAll());
        Assert.Equal("PLATFORM\n\nNEW PERSONA", agent.Instructions);
        Assert.Equal(1, CountOccurrences(agent.Instructions, "NEW PERSONA"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}