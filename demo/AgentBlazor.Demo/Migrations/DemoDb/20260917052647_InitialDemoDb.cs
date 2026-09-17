using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentBlazor.Demo.Migrations.DemoDb
{
    /// <inheritdoc />
    public partial class InitialDemoDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demo_agent_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AllowedComponentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowedActionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowedCapabilityActionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowedDataSchemasJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_agent_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "demo_conversation_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    LastActivityAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_conversation_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "demo_conversation_turns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AgentResponse = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlannedActionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionResultsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionPlanJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GeneratedUiJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TurnSequence = table.Column<int>(type: "int", nullable: false),
                    PromptTokens = table.Column<long>(type: "bigint", nullable: true),
                    CompletionTokens = table.Column<long>(type: "bigint", nullable: true),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: true),
                    CachedInputTokens = table.Column<long>(type: "bigint", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    EstimatedCostCurrency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    InputTokenCostPerMillion = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    OutputTokenCostPerMillion = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CachedInputTokenCostPerMillion = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
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
