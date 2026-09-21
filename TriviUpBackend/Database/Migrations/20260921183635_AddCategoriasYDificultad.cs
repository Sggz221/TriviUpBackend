using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TriviUpBackend.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoriasYDificultad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Dificultad",
                table: "preguntas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CategoriaId",
                table: "banco_preguntas",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dificultad",
                table: "banco_preguntas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "banco_categorias",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatorId = table.Column<long>(type: "bigint", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banco_categorias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_banco_categorias_users_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Migración de datos: las etiquetas pasan a categorías (una por pregunta). Se conserva la
            // primera etiqueta de cada pregunta ("|a|b|" -> "a"); las demás se descartan.
            migrationBuilder.Sql(@"
                INSERT INTO banco_categorias (""CreatorId"", ""Nombre"")
                SELECT DISTINCT p.""CreatorId"", split_part(trim(both '|' from p.""EtiquetasTexto""), '|', 1)
                FROM banco_preguntas p
                WHERE p.""EtiquetasTexto"" <> '';");

            migrationBuilder.Sql(@"
                UPDATE banco_preguntas p
                SET ""CategoriaId"" = c.""Id""
                FROM banco_categorias c
                WHERE p.""EtiquetasTexto"" <> ''
                  AND c.""CreatorId"" = p.""CreatorId""
                  AND c.""Nombre"" = split_part(trim(both '|' from p.""EtiquetasTexto""), '|', 1);");

            migrationBuilder.DropColumn(
                name: "EtiquetasTexto",
                table: "banco_preguntas");

            migrationBuilder.CreateIndex(
                name: "IX_banco_preguntas_CategoriaId",
                table: "banco_preguntas",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_banco_categorias_CreatorId",
                table: "banco_categorias",
                column: "CreatorId");

            // Nombre único por usuario sin distinguir mayúsculas (EF no modela índices sobre expresiones)
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_banco_categorias_CreatorId_lower_Nombre""
                ON banco_categorias (""CreatorId"", lower(""Nombre""));");

            migrationBuilder.AddForeignKey(
                name: "FK_banco_preguntas_banco_categorias_CategoriaId",
                table: "banco_preguntas",
                column: "CategoriaId",
                principalTable: "banco_categorias",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EtiquetasTexto",
                table: "banco_preguntas",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            // Cada categoría vuelve a ser la etiqueta de sus preguntas
            migrationBuilder.Sql(@"
                UPDATE banco_preguntas p
                SET ""EtiquetasTexto"" = '|' || lower(c.""Nombre"") || '|'
                FROM banco_categorias c
                WHERE p.""CategoriaId"" = c.""Id"";");

            migrationBuilder.DropForeignKey(
                name: "FK_banco_preguntas_banco_categorias_CategoriaId",
                table: "banco_preguntas");

            migrationBuilder.DropTable(
                name: "banco_categorias");

            migrationBuilder.DropIndex(
                name: "IX_banco_preguntas_CategoriaId",
                table: "banco_preguntas");

            migrationBuilder.DropColumn(
                name: "Dificultad",
                table: "preguntas");

            migrationBuilder.DropColumn(
                name: "CategoriaId",
                table: "banco_preguntas");

            migrationBuilder.DropColumn(
                name: "Dificultad",
                table: "banco_preguntas");
        }
    }
}
