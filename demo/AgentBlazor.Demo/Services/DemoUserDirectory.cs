namespace AgentBlazor.Demo.Services;

/// <summary>
/// Static in-memory demo user directory for the user-context showcase. The Demo has no real
/// authentication, so a small deterministic directory stands in for <c>ClaimsPrincipal</c> /
/// user-profile lookups: the user id flows as a plain string through
/// <c>AgentTurnRequest.UserId</c> / <c>GetEffectiveUserId()</c>, and unknown users resolve to
/// the default profile.
/// </summary>
public static class DemoUserDirectory
{
    /// <summary>Identity of the default demo user when no user id is supplied on the turn.</summary>
    public const string DefaultUserId = "demo-user";

    /// <summary>A demo user profile — the identity layer of the user-scoped context.</summary>
    public sealed record DemoUserProfile(
        string Id,
        string DisplayName,
        string Role,
        string Region,
        string Locale);

    private static readonly IReadOnlyDictionary<string, DemoUserProfile> Profiles =
        new Dictionary<string, DemoUserProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultUserId] = new(DefaultUserId, "Alex Rivera", "Compliance Analyst", "EMEA", "en-US"),
            ["demo-ops"] = new("demo-ops", "Priya Sharma", "Operations Lead", "APAC", "en-IN"),
            ["demo-admin"] = new("demo-admin", "Sam Okafor", "Tenant Admin", "AMER", "en-US"),
        };

    /// <summary>All known profiles — used by the demo user picker in the Agent Builder page.</summary>
    public static IEnumerable<DemoUserProfile> All => Profiles.Values;

    /// <summary>
    /// Resolves a user id to a profile, falling back to <see cref="DefaultUserId"/> for
    /// unknown or null ids so the showcase is always deterministic.
    /// </summary>
    public static DemoUserProfile Resolve(string? userId)
        => userId is not null && Profiles.TryGetValue(userId, out var profile)
            ? profile
            : Profiles[DefaultUserId];
}