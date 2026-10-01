using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TriviUpBackend.Database.Migrations
{
    /// <inheritdoc />
    public partial class PreguntaTipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tipo",
                table: "preguntas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "normal");

            // Las preguntas de las rondas dinámicas pasan a ser preguntas de pulsador.
            migrationBuilder.Sql("UPDATE preguntas SET \"Tipo\" = 'pulsador' WHERE \"FaseDinamica\";");

            migrationBuilder.DropColumn(
                name: "FaseDinamica",
                table: "preguntas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FaseDinamica",
                table: "preguntas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE preguntas SET \"FaseDinamica\" = TRUE WHERE \"Tipo\" = 'pulsador';");

            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "preguntas");
        }
    }
}
