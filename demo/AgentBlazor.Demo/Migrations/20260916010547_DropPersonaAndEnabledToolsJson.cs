using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentBlazor.Demo.Migrations
{
    /// <inheritdoc />
    public partial class DropPersonaAndEnabledToolsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnabledToolsJson",
                table: "demo_agent_definitions");

            migrationBuilder.DropColumn(
                name: "Persona",
                table: "demo_agent_definitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnabledToolsJson",
                table: "demo_agent_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Persona",
                table: "demo_agent_definitions",
                type: "text",
                nullable: true);
        }
    }
}
