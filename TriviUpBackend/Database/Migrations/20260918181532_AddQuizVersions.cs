using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TriviUpBackend.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VersionPublicada",
                table: "quizzes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Los cuestionarios ya publicados pasan a ser la versión 1
            migrationBuilder.Sql("UPDATE quizzes SET \"VersionPublicada\" = 1 WHERE \"EsBorrador\" = FALSE;");

            migrationBuilder.CreateTable(
                name: "quiz_versions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuizId = table.Column<long>(type: "bigint", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: true),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EsPublico = table.Column<bool>(type: "boolean", nullable: false),
                    Contenido = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quiz_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quiz_versions_quizzes_QuizId",
                        column: x => x.QuizId,
                        principalTable: "quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 1L,
                column: "VersionPublicada",
                value: 1);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 2L,
                column: "VersionPublicada",
                value: 1);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 3L,
                column: "VersionPublicada",
                value: 1);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 4L,
                column: "VersionPublicada",
                value: 1);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 5L,
                column: "VersionPublicada",
                value: 1);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 6L,
                column: "VersionPublicada",
                value: 1);

            migrationBuilder.CreateIndex(
                name: "IX_quiz_versions_QuizId_Borrador",
                table: "quiz_versions",
                column: "QuizId",
                unique: true,
                filter: "\"Estado\" = 'Borrador'");

            migrationBuilder.CreateIndex(
                name: "IX_quiz_versions_QuizId_Numero",
                table: "quiz_versions",
                columns: new[] { "QuizId", "Numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quiz_versions");

            migrationBuilder.DropColumn(
                name: "VersionPublicada",
                table: "quizzes");
        }
    }
}
