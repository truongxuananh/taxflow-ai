using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxFlow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantConversationContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextJson",
                table: "ChatMessages",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.Sql("UPDATE ChatMessages SET ContextJson = '{}' WHERE ContextJson IS NULL;");
            migrationBuilder.AlterColumn<string>(
                name: "ContextJson", table: "ChatMessages", type: "longtext", nullable: false,
                oldClrType: typeof(string), oldType: "longtext", oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextJson",
                table: "ChatMessages");
        }
    }
}
