using AgentBlazor.Demo.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AgentBlazor.Demo.Services;

internal sealed class DemoWorkflowDatabaseSeeder(IDbContextFactory<DemoWorkflowDbContext> dbContextFactory)
{
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await InitializationGate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            // NOTE: Do NOT call EnsureCreatedAsync here — Program.cs already ran MigrateAsync
            // on this context. EF Core warns against mixing EnsureCreated with Migrate (the
            // former bypasses the migration history table). The DDL below uses IF NOT EXISTS
            // guards to safely add tables/columns not yet captured in migrations.
            await EnsureDojoWorkspaceSchemaAsync(db, cancellationToken);
            await EnsureFileWorkflowSchemaAsync(db, cancellationToken);
        }
        finally
        {
            InitializationGate.Release();
        }
    }

    private static async Task EnsureDojoWorkspaceSchemaAsync(
        DemoWorkflowDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'dojo_workspaces')
            BEGIN
                CREATE TABLE dojo_workspaces (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    SessionKey NVARCHAR(256) NOT NULL,
                    Title NVARCHAR(MAX) NOT NULL,
                    Minutes INT NOT NULL,
                    Difficulty NVARCHAR(MAX) NOT NULL,
                    HighProtein BIT NOT NULL,
                    LowCarb BIT NOT NULL,
                    Spicy BIT NOT NULL,
                    Vegetarian BIT NOT NULL,
                    BudgetFriendly BIT NOT NULL DEFAULT 0,
                    OnePotMeal BIT NOT NULL DEFAULT 0,
                    Vegan BIT NOT NULL DEFAULT 0,
                    LastSavedUtc DATETIME2 NULL,
                    CreatedUtc DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedUtc DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                );
            END
            """,
            cancellationToken);
        if (!await IndexExistsAsync(db, "IX_dojo_workspaces_SessionKey", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IX_dojo_workspaces_SessionKey ON dojo_workspaces (SessionKey);",
                cancellationToken);
        }
        await TryAddColumnAsync(db, "dojo_workspaces", "BudgetFriendly", "BIT NOT NULL DEFAULT 0", cancellationToken);
        await TryAddColumnAsync(db, "dojo_workspaces", "OnePotMeal", "BIT NOT NULL DEFAULT 0", cancellationToken);
        await TryAddColumnAsync(db, "dojo_workspaces", "Vegan", "BIT NOT NULL DEFAULT 0", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'dojo_ingredients')
            BEGIN
                CREATE TABLE dojo_ingredients (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    SessionKey NVARCHAR(256) NOT NULL,
                    IngredientId NVARCHAR(256) NOT NULL,
                    Name NVARCHAR(MAX) NOT NULL,
                    Amount NVARCHAR(MAX) NOT NULL,
                    Optional BIT NOT NULL,
                    Notes NVARCHAR(MAX) NOT NULL,
                    SortOrder INT NOT NULL
                );
            END
            """,
            cancellationToken);
        if (!await IndexExistsAsync(db, "IX_dojo_ingredients_SessionKey", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_dojo_ingredients_SessionKey ON dojo_ingredients (SessionKey);",
                cancellationToken);
        }
        if (!await IndexExistsAsync(db, "IX_dojo_ingredients_SessionKey_IngredientId", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IX_dojo_ingredients_SessionKey_IngredientId ON dojo_ingredients (SessionKey, IngredientId);",
                cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'dojo_steps')
            BEGIN
                CREATE TABLE dojo_steps (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    SessionKey NVARCHAR(256) NOT NULL,
                    StepNumber INT NOT NULL,
                    Text NVARCHAR(MAX) NOT NULL
                );
            END
            """,
            cancellationToken);
        if (!await IndexExistsAsync(db, "IX_dojo_steps_SessionKey", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_dojo_steps_SessionKey ON dojo_steps (SessionKey);",
                cancellationToken);
        }
        if (!await IndexExistsAsync(db, "IX_dojo_steps_SessionKey_StepNumber", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IX_dojo_steps_SessionKey_StepNumber ON dojo_steps (SessionKey, StepNumber);",
                cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'dojo_run_notes')
            BEGIN
                CREATE TABLE dojo_run_notes (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    SessionKey NVARCHAR(256) NOT NULL,
                    TimestampUtc DATETIME2 NOT NULL,
                    Message NVARCHAR(MAX) NOT NULL
                );
            END
            """,
            cancellationToken);
        if (!await IndexExistsAsync(db, "IX_dojo_run_notes_SessionKey", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_dojo_run_notes_SessionKey ON dojo_run_notes (SessionKey);",
                cancellationToken);
        }
        if (!await IndexExistsAsync(db, "IX_dojo_run_notes_SessionKey_TimestampUtc", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_dojo_run_notes_SessionKey_TimestampUtc ON dojo_run_notes (SessionKey, TimestampUtc);",
                cancellationToken);
        }
    }

    private static async Task EnsureFileWorkflowSchemaAsync(
        DemoWorkflowDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'demo_file_workflow_files')
            BEGIN
                CREATE TABLE demo_file_workflow_files (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    SessionKey NVARCHAR(256) NOT NULL,
                    FileName NVARCHAR(MAX) NOT NULL,
                    UploadMode NVARCHAR(MAX) NOT NULL,
                    StorageToken NVARCHAR(MAX) NULL,
                    AddedUtc DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedUtc DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                );
            END
            """,
            cancellationToken);
        if (!await IndexExistsAsync(db, "IX_demo_file_workflow_files_SessionKey", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_demo_file_workflow_files_SessionKey ON demo_file_workflow_files (SessionKey);",
                cancellationToken);
        }
        if (!await IndexExistsAsync(db, "IX_demo_file_workflow_files_SessionKey_FileName", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IX_demo_file_workflow_files_SessionKey_FileName ON demo_file_workflow_files (SessionKey, FileName);",
                cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'demo_file_workflow_events')
            BEGIN
                CREATE TABLE demo_file_workflow_events (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    SessionKey NVARCHAR(256) NOT NULL,
                    TimestampUtc DATETIME2 NOT NULL,
                    EventType NVARCHAR(MAX) NOT NULL,
                    FileName NVARCHAR(MAX) NOT NULL,
                    Message NVARCHAR(MAX) NOT NULL
                );
            END
            """,
            cancellationToken);
        if (!await IndexExistsAsync(db, "IX_demo_file_workflow_events_SessionKey", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_demo_file_workflow_events_SessionKey ON demo_file_workflow_events (SessionKey);",
                cancellationToken);
        }
        if (!await IndexExistsAsync(db, "IX_demo_file_workflow_events_SessionKey_TimestampUtc", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_demo_file_workflow_events_SessionKey_TimestampUtc ON demo_file_workflow_events (SessionKey, TimestampUtc);",
                cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'demo_file_workflow_jobs')
            BEGIN
                CREATE TABLE demo_file_workflow_jobs (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    SessionKey NVARCHAR(256) NOT NULL,
                    JobId NVARCHAR(256) NOT NULL,
                    Operation NVARCHAR(MAX) NOT NULL,
                    FileName NVARCHAR(MAX) NOT NULL,
                    UploadMode NVARCHAR(MAX) NOT NULL,
                    Status NVARCHAR(MAX) NOT NULL,
                    StorageToken NVARCHAR(MAX) NULL,
                    Message NVARCHAR(MAX) NOT NULL,
                    CreatedUtc DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedUtc DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    CompletedUtc DATETIME2 NULL
                );
            END
            """,
            cancellationToken);
        if (!await IndexExistsAsync(db, "IX_demo_file_workflow_jobs_JobId", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IX_demo_file_workflow_jobs_JobId ON demo_file_workflow_jobs (JobId);",
                cancellationToken);
        }
        if (!await IndexExistsAsync(db, "IX_demo_file_workflow_jobs_SessionKey", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_demo_file_workflow_jobs_SessionKey ON demo_file_workflow_jobs (SessionKey);",
                cancellationToken);
        }
        if (!await IndexExistsAsync(db, "IX_demo_file_workflow_jobs_SessionKey_UpdatedUtc", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IX_demo_file_workflow_jobs_SessionKey_UpdatedUtc ON demo_file_workflow_jobs (SessionKey, UpdatedUtc);",
                cancellationToken);
        }
    }

    private static async Task<bool> IndexExistsAsync(
        DemoWorkflowDbContext db,
        string indexName,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE name = @indexName;";
            var parameter = new SqlParameter("@indexName", indexName);
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        DemoWorkflowDbContext db,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName
                """;
            command.Parameters.Add(new SqlParameter("@tableName", tableName));
            command.Parameters.Add(new SqlParameter("@columnName", columnName));

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    // Table/column names are hardcoded constants — SQL parameterization is not applicable
    // for DDL identifiers (ALTER TABLE/ADD COLUMN only accept literal identifiers).
    private static async Task TryAddColumnAsync(
        DemoWorkflowDbContext db,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(db, tableName, columnName, cancellationToken))
        {
            return;
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};",
                cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 2705) // Column already exists
        {
            // Another startup path may have added the column after the schema check.
        }
    }
}
