using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentBlazor.Demo.Migrations.WorkflowDb
{
    /// <inheritdoc />
    public partial class InitialDemoWorkflowDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demo_file_workflow_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_file_workflow_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "demo_file_workflow_files",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UploadMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StorageToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AddedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_file_workflow_files", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "demo_file_workflow_jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    JobId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StorageToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_file_workflow_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dojo_ingredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IngredientId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Optional = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dojo_ingredients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dojo_run_notes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dojo_run_notes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dojo_steps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StepNumber = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dojo_steps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dojo_workspaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    Difficulty = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HighProtein = table.Column<bool>(type: "bit", nullable: false),
                    LowCarb = table.Column<bool>(type: "bit", nullable: false),
                    Spicy = table.Column<bool>(type: "bit", nullable: false),
                    Vegetarian = table.Column<bool>(type: "bit", nullable: false),
                    BudgetFriendly = table.Column<bool>(type: "bit", nullable: false),
                    OnePotMeal = table.Column<bool>(type: "bit", nullable: false),
                    Vegan = table.Column<bool>(type: "bit", nullable: false),
                    LastSavedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dojo_workspaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_demo_file_workflow_events_SessionKey",
                table: "demo_file_workflow_events",
                column: "SessionKey");

            migrationBuilder.CreateIndex(
                name: "IX_demo_file_workflow_events_SessionKey_TimestampUtc",
                table: "demo_file_workflow_events",
                columns: new[] { "SessionKey", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_demo_file_workflow_files_SessionKey",
                table: "demo_file_workflow_files",
                column: "SessionKey");

            migrationBuilder.CreateIndex(
                name: "IX_demo_file_workflow_files_SessionKey_FileName",
                table: "demo_file_workflow_files",
                columns: new[] { "SessionKey", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demo_file_workflow_jobs_JobId",
                table: "demo_file_workflow_jobs",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demo_file_workflow_jobs_SessionKey",
                table: "demo_file_workflow_jobs",
                column: "SessionKey");

            migrationBuilder.CreateIndex(
                name: "IX_demo_file_workflow_jobs_SessionKey_UpdatedUtc",
                table: "demo_file_workflow_jobs",
                columns: new[] { "SessionKey", "UpdatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_dojo_ingredients_SessionKey",
                table: "dojo_ingredients",
                column: "SessionKey");

            migrationBuilder.CreateIndex(
                name: "IX_dojo_ingredients_SessionKey_IngredientId",
                table: "dojo_ingredients",
                columns: new[] { "SessionKey", "IngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dojo_run_notes_SessionKey",
                table: "dojo_run_notes",
                column: "SessionKey");

            migrationBuilder.CreateIndex(
                name: "IX_dojo_run_notes_SessionKey_TimestampUtc",
                table: "dojo_run_notes",
                columns: new[] { "SessionKey", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_dojo_steps_SessionKey",
                table: "dojo_steps",
                column: "SessionKey");

            migrationBuilder.CreateIndex(
                name: "IX_dojo_steps_SessionKey_StepNumber",
                table: "dojo_steps",
                columns: new[] { "SessionKey", "StepNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dojo_workspaces_SessionKey",
                table: "dojo_workspaces",
                column: "SessionKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demo_file_workflow_events");

            migrationBuilder.DropTable(
                name: "demo_file_workflow_files");

            migrationBuilder.DropTable(
                name: "demo_file_workflow_jobs");

            migrationBuilder.DropTable(
                name: "dojo_ingredients");

            migrationBuilder.DropTable(
                name: "dojo_run_notes");

            migrationBuilder.DropTable(
                name: "dojo_steps");

            migrationBuilder.DropTable(
                name: "dojo_workspaces");
        }
    }
}
