using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentBlazor.Demo.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "demo_conversation_sessions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "demo_conversation_sessions");
        }
    }
}
