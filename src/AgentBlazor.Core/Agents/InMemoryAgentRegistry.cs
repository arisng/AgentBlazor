using System.Collections.Concurrent;

namespace AgentBlazor.Agents;

public sealed class InMemoryAgentRegistry : IAsyncAgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<AgentRegistration> GetAll() => _registrations.Values.ToArray();

    // Overridden rather than inherited: the default forwards through GetAll(), adding a
    // Task allocation to a call that is already allocation-backed by Values.ToArray().
    public Task<IReadOnlyCollection<AgentRegistration>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<AgentRegistration>>(_registrations.Values.ToArray());

    public bool TryGet(string name, out AgentRegistration registration) =>
        _registrations.TryGetValue(name, out registration!);

    // Overridden rather than inherited: the default materializes every registration and
    // scans linearly, which would turn this O(1) keyed probe into O(n) on the per-turn
    // resolution path.
    public Task<bool> TryGetAsync(
        string name,
        Func<AgentRegistration, bool> onFound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onFound);

        return Task.FromResult(
            _registrations.TryGetValue(name, out var registration) && onFound(registration));
    }

    public void AddOrUpdate(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registrations[registration.Name] = registration;
    }

    // Overridden rather than inherited: writes into a ConcurrentDictionary cannot block,
    // so the default's thread-pool hop would add latency without removing any wait.
    public Task AddOrUpdateAsync(
        AgentRegistration registration,
        CancellationToken cancellationToken = default)
    {
        AddOrUpdate(registration);
        return Task.CompletedTask;
    }
}
