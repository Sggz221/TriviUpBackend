using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TriviUpBackend.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddFaseDinamica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FaseDinamica",
                table: "preguntas",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaseDinamica",
                table: "preguntas");
        }
    }
}
