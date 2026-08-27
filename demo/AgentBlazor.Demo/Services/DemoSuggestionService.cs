using AgentBlazor.Core.Paid;

namespace AgentBlazor.Demo.Services;

public class DemoSuggestionService : IAdaptiveSuggestionService
{
    public Task<IReadOnlyList<AgentSuggestion>> GetSuggestionsAsync(
        string sessionId,
        string? userId,
        string? currentContext,
        CancellationToken ct = default)
    {
        var suggestions = currentContext?.ToLowerInvariant() switch
        {
            var ctx when ctx?.Contains("support-inbox") == true => new[]
            {
                new AgentSuggestion("Show open tickets", 0.85f, "heuristic"),
                new AgentSuggestion("Explain the queue", 0.70f, "heuristic"),
                new AgentSuggestion("Draft a reply", 0.65f, "heuristic"),
            },
            var ctx when ctx?.Contains("supplier-compliance") == true => new[]
            {
                new AgentSuggestion("Explain supplier risk", 0.85f, "heuristic"),
                new AgentSuggestion("Recover blocked suppliers", 0.75f, "heuristic"),
                new AgentSuggestion("Prepare remediation", 0.65f, "heuristic"),
            },
            var ctx when ctx?.Contains("incident-escalation") == true => new[]
            {
                new AgentSuggestion("Summarize incident triage", 0.85f, "heuristic"),
                new AgentSuggestion("Focus on evidence", 0.70f, "heuristic"),
                new AgentSuggestion("Assign owner", 0.65f, "heuristic"),
            },
            var ctx when ctx?.Contains("file-audit-bundle") == true => new[]
            {
                new AgentSuggestion("Explain evidence blockers", 0.85f, "heuristic"),
                new AgentSuggestion("Recover missing file", 0.75f, "heuristic"),
                new AgentSuggestion("Prepare audit bundle", 0.65f, "heuristic"),
            },
            var ctx when ctx?.Contains("recipe-release") == true => new[]
            {
                new AgentSuggestion("Assess recipe readiness", 0.85f, "heuristic"),
                new AgentSuggestion("Recover missing evidence", 0.75f, "heuristic"),
                new AgentSuggestion("Prepare release draft", 0.65f, "heuristic"),
            },
            var ctx when ctx?.Contains("response-orchestration") == true => new[]
            {
                new AgentSuggestion("Assess cross-system readiness", 0.85f, "heuristic"),
                new AgentSuggestion("Advance next stage", 0.75f, "heuristic"),
                new AgentSuggestion("Prepare response packet", 0.65f, "heuristic"),
            },
            var ctx when ctx?.Contains("release-dossier") == true => new[]
            {
                new AgentSuggestion("Assess release readiness", 0.85f, "heuristic"),
                new AgentSuggestion("Advance guided stage", 0.75f, "heuristic"),
                new AgentSuggestion("Prepare release dossier", 0.65f, "heuristic"),
            },
            var ctx when ctx?.Contains("components") == true => new[]
            {
                new AgentSuggestion("Show component catalog", 0.85f, "heuristic"),
                new AgentSuggestion("Try the DataGrid", 0.70f, "heuristic"),
                new AgentSuggestion("Test the Form component", 0.65f, "heuristic"),
            },
            _ => new[]
            {
                new AgentSuggestion("What can you do?", 0.80f, "heuristic"),
                new AgentSuggestion("Show me the available workflows", 0.70f, "heuristic"),
                new AgentSuggestion("Help me get started", 0.65f, "heuristic"),
            },
        };

        return Task.FromResult<IReadOnlyList<AgentSuggestion>>(suggestions);
    }
}
