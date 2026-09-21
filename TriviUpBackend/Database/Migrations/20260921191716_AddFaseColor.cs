using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TriviUpBackend.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddFaseColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FaseColor",
                table: "preguntas",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaseColor",
                table: "preguntas");
        }
    }
}
