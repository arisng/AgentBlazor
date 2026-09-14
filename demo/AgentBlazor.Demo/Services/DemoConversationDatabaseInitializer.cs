using AgentBlazor.Demo.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Creates and upgrades the SQLite schema for <see cref="DemoConversationDbContext"/> at startup.
/// <para>
/// The Demo deliberately has no EF Core migrations, so <c>EnsureCreatedAsync</c> creates the
/// full schema for a fresh database but never evolves an existing one. The additive column
/// upgrade below keeps pre-existing <c>agentblazor-demo-conversations.db</c> files working
/// after the token-usage columns were introduced — without it, an existing database would
/// fail at runtime with "no such column: PromptTokens".
/// </para>
/// <para>
/// This mirrors the additive-upgrade approach already used by
/// <see cref="DemoWorkflowDatabaseSeeder"/> for <c>dojo_workspaces</c>.
/// </para>
/// </summary>
internal sealed class DemoConversationDatabaseInitializer(IDbContextFactory<DemoConversationDbContext> dbContextFactory)
{
    /// <summary>
    /// Additive columns introduced after the initial schema, in the order they are applied.
    /// Each statement is only run when its column is missing, so this is safe on every
    /// startup and on both fresh and pre-existing database files.
    /// </summary>
    private static readonly (string Column, string AddColumnSql)[] TurnUsageColumns =
    [
        ("PromptTokens", "ALTER TABLE demo_conversation_turns ADD COLUMN PromptTokens INTEGER NULL;"),
        ("CompletionTokens", "ALTER TABLE demo_conversation_turns ADD COLUMN CompletionTokens INTEGER NULL;"),
        ("TotalTokens", "ALTER TABLE demo_conversation_turns ADD COLUMN TotalTokens INTEGER NULL;"),
        ("CachedInputTokens", "ALTER TABLE demo_conversation_turns ADD COLUMN CachedInputTokens INTEGER NULL;"),
        ("EstimatedCost", "ALTER TABLE demo_conversation_turns ADD COLUMN EstimatedCost REAL NULL;"),
        ("EstimatedCostCurrency", "ALTER TABLE demo_conversation_turns ADD COLUMN EstimatedCostCurrency TEXT NULL;"),
        ("InputTokenCostPerMillion", "ALTER TABLE demo_conversation_turns ADD COLUMN InputTokenCostPerMillion REAL NULL;"),
        ("OutputTokenCostPerMillion", "ALTER TABLE demo_conversation_turns ADD COLUMN OutputTokenCostPerMillion REAL NULL;"),
        ("CachedInputTokenCostPerMillion", "ALTER TABLE demo_conversation_turns ADD COLUMN CachedInputTokenCostPerMillion REAL NULL;")
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureTurnUsageColumnsAsync(db, cancellationToken);
    }

    private static async Task EnsureTurnUsageColumnsAsync(
        DemoConversationDbContext db,
        CancellationToken cancellationToken)
    {
        var existing = await GetTurnColumnNamesAsync(db, cancellationToken);
        if (existing.Count == 0)
        {
            // The turns table does not exist, so there is nothing to upgrade. Adding a
            // column here would fail; EnsureCreatedAsync owns table creation.
            return;
        }

        foreach (var (column, addColumnSql) in TurnUsageColumns)
        {
            if (existing.Contains(column))
            {
                continue;
            }

            try
            {
                await db.Database.ExecuteSqlRawAsync(addColumnSql, cancellationToken);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
                                             ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Another startup path added the column after the schema check.
            }
        }
    }

    private static async Task<HashSet<string>> GetTurnColumnNamesAsync(
        DemoConversationDbContext db,
        CancellationToken cancellationToken)
    {
        // Scalar SqlQueryRaw results must be aliased as "Value".
        var names = await db.Database
            .SqlQueryRaw<string>(
                "SELECT name AS \"Value\" FROM pragma_table_info('demo_conversation_turns')")
            .ToListAsync(cancellationToken);

        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }
}