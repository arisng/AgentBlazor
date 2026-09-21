using AgentBlazor.Demo.Services;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// Locks the LIVE property of the user-context domain layer: the workflow services expose
/// their current business state via <see cref="IProvideLiveUserContext"/> (async contract),
/// and the readings CHANGE when the agent executes actions — this is runtime context, not
/// static seed data.
/// </summary>
public class LiveUserContextTests
{
    [Fact]
    public async Task SupportInbox_GetLiveUserContextAsync_ReflectsActionMutations()
    {
        var workflow = new SupportInboxWorkflowService();

        // No focus yet — nothing highlighted or escalated.
        var before = await workflow.GetLiveUserContextAsync();
        Assert.Equal("0", before["support_inbox.highlighted"]);
        Assert.Equal("0", before["support_inbox.escalated"]);

        // The agent runs show_open_tickets → at-risk tickets are highlighted.
        workflow.FocusOpenTickets(7);
        var afterFocus = await workflow.GetLiveUserContextAsync();
        Assert.True(int.Parse(afterFocus["support_inbox.highlighted"]!) > 0, "focus must highlight at-risk tickets");

        // The agent runs the escalation playbook → the escalated count rises.
        var escalatedBefore = int.Parse(afterFocus["support_inbox.escalated"]!);
        workflow.ApplyEscalationPlaybook();
        var afterEscalation = await workflow.GetLiveUserContextAsync();
        Assert.True(int.Parse(afterEscalation["support_inbox.escalated"]!) > escalatedBefore, "escalation must raise the live escalated count");
    }

    [Fact]
    public async Task SupplierCompliance_GetLiveUserContextAsync_ReflectsActionMutations()
    {
        var workflow = new SupplierComplianceWorkflowService();

        var before = await workflow.GetLiveUserContextAsync();
        Assert.Equal("0", before["supplier_compliance.recovered"]);

        workflow.FocusAtRiskSuppliers(30);
        var afterFocus = await workflow.GetLiveUserContextAsync();
        Assert.True(int.Parse(afterFocus["supplier_compliance.at_risk"]!) > 0, "focus must surface at-risk suppliers");
    }
}