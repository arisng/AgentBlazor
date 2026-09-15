using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentBlazor.Demo.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demo_agent_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Instructions = table.Column<string>(type: "TEXT", nullable: true),
                    AllowedComponentsJson = table.Column<string>(type: "text", nullable: false),
                    AllowedActionsJson = table.Column<string>(type: "text", nullable: false),
                    AllowedDataSchemasJson = table.Column<string>(type: "text", nullable: false),
                    Persona = table.Column<string>(type: "text", nullable: true),
                    EnabledToolsJson = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_agent_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "demo_conversation_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastActivityAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_conversation_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "demo_conversation_turns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TurnId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserMessage = table.Column<string>(type: "TEXT", nullable: false),
                    AgentResponse = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedActionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutionResultsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutionPlanJson = table.Column<string>(type: "TEXT", nullable: true),
                    GeneratedUiJson = table.Column<string>(type: "TEXT", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TurnSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    PromptTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletionTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    CachedInputTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    EstimatedCost = table.Column<double>(type: "REAL", nullable: true),
                    EstimatedCostCurrency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    InputTokenCostPerMillion = table.Column<double>(type: "REAL", nullable: true),
                    OutputTokenCostPerMillion = table.Column<double>(type: "REAL", nullable: true),
                    CachedInputTokenCostPerMillion = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_conversation_turns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_demo_agent_definitions_Name",
                table: "demo_agent_definitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demo_agent_definitions_TenantId",
                table: "demo_agent_definitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_demo_conversation_sessions_LastActivityAtUtc",
                table: "demo_conversation_sessions",
                column: "LastActivityAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_demo_conversation_sessions_SessionId",
                table: "demo_conversation_sessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_demo_conversation_sessions_UserId",
                table: "demo_conversation_sessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_demo_conversation_turns_SessionId",
                table: "demo_conversation_turns",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_demo_conversation_turns_SessionId_TurnId",
                table: "demo_conversation_turns",
                columns: new[] { "SessionId", "TurnId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demo_agent_definitions");

            migrationBuilder.DropTable(
                name: "demo_conversation_sessions");

            migrationBuilder.DropTable(
                name: "demo_conversation_turns");
        }
    }
}
