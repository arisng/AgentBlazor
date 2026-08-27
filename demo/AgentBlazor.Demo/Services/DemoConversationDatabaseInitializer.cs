using AgentBlazor.Demo.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Creates the SQLite schema for <see cref="DemoConversationDbContext"/> at startup.
/// </summary>
internal sealed class DemoConversationDatabaseInitializer(IDbContextFactory<DemoConversationDbContext> dbContextFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}