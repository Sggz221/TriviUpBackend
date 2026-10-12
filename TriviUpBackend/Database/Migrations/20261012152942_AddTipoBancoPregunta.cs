using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TriviUpBackend.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTipoBancoPregunta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tipo",
                table: "banco_preguntas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "normal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "banco_preguntas");
        }
    }
}
