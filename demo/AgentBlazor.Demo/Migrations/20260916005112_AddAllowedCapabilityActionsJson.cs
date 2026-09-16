using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentBlazor.Demo.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowedCapabilityActionsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedCapabilityActionsJson",
                table: "demo_agent_definitions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedCapabilityActionsJson",
                table: "demo_agent_definitions");
        }
    }
}
