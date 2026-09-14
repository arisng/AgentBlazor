using AgentBlazor.Core.Runtime.Conversation;
using Microsoft.Extensions.AI;

namespace AgentBlazor.Core.Tests;

public class ConversationTurnUsageTests
{
    [Fact]
    public void FromUsageDetails_WhenUsageIsNull_ReturnsNull()
    {
        Assert.Null(ConversationTurnUsage.FromUsageDetails(null));
    }

    [Fact]
    public void FromUsageDetails_WhenNoCountsReported_ReturnsNull()
    {
        // A provider that returns an empty usage object carries no information; persisting
        // it would be indistinguishable from "reported zero tokens".
        Assert.Null(ConversationTurnUsage.FromUsageDetails(new UsageDetails()));
    }

    [Fact]
    public void FromUsageDetails_ProjectsEveryReportedCount()
    {
        var usage = new UsageDetails
        {
            InputTokenCount = 1200,
            OutputTokenCount = 340,
            TotalTokenCount = 1540,
            CachedInputTokenCount = 800
        };

        var projected = ConversationTurnUsage.FromUsageDetails(usage);

        Assert.NotNull(projected);
        Assert.True(projected.HasAnyCounts);
        Assert.Equal(1200L, projected.InputTokens);
        Assert.Equal(340L, projected.OutputTokens);
        Assert.Equal(1540L, projected.TotalTokens);
        Assert.Equal(800L, projected.CachedInputTokens);
    }

    [Fact]
    public void FromUsageDetails_WhenOnlyOutputReported_KeepsPartialUsage()
    {
        // Providers report partial usage; a missing count must stay null rather than
        // being defaulted to zero.
        var projected = ConversationTurnUsage.FromUsageDetails(
            new UsageDetails { OutputTokenCount = 42 });

        Assert.NotNull(projected);
        Assert.Null(projected.InputTokens);
        Assert.Null(projected.TotalTokens);
        Assert.Null(projected.CachedInputTokens);
        Assert.Equal(42L, projected.OutputTokens);
    }

    [Fact]
    public void HasAnyCounts_IsFalseForAnEmptyRecord()
    {
        Assert.False(new ConversationTurnUsage().HasAnyCounts);
        Assert.True(new ConversationTurnUsage { TotalTokens = 0 }.HasAnyCounts);
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var left = new ConversationTurnUsage { InputTokens = 1, OutputTokens = 2, TotalTokens = 3 };
        var right = new ConversationTurnUsage { InputTokens = 1, OutputTokens = 2, TotalTokens = 3 };

        Assert.Equal(left, right);
        Assert.NotEqual(left, right with { OutputTokens = 4 });
    }

    [Fact]
    public void ConversationTurn_DefaultsToNoUsage()
    {
        var turn = new ConversationTurn
        {
            Timestamp = DateTime.UtcNow,
            UserMessage = "hello",
            AgentResponse = "hi"
        };

        // Turns that never reached the model (short-circuited / no-agent / approval
        // continuation) must be distinguishable from turns that cost nothing.
        Assert.Null(turn.Usage);
    }
}