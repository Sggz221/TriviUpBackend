using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TriviUpBackend.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    QuizId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    QuizTitle = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlayerResultsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsBanned = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    GoogleId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProfilePhotoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quizzes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GameCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatorId = table.Column<long>(type: "bigint", nullable: false),
                    EsPublico = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EsBorrador = table.Column<bool>(type: "boolean", nullable: false),
                    Visitas = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Likes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quizzes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quizzes_users_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "preguntas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuizId = table.Column<long>(type: "bigint", nullable: false),
                    CreatorId = table.Column<long>(type: "bigint", nullable: false),
                    NumeroPregunta = table.Column<int>(type: "integer", nullable: false),
                    Enunciado = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ImagenUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preguntas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_preguntas_quizzes_QuizId",
                        column: x => x.QuizId,
                        principalTable: "quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_preguntas_users_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "respuestas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PreguntaId = table.Column<long>(type: "bigint", nullable: false),
                    Texto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EsCorrecta = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_respuestas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_respuestas_preguntas_PreguntaId",
                        column: x => x.PreguntaId,
                        principalTable: "preguntas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "Id", "CreatedAt", "Email", "GoogleId", "IsBanned", "IsDeleted", "LastLoginAt", "PasswordHash", "ProfilePhotoUrl", "Role", "UpdatedAt", "Username" },
                values: new object[,]
                {
                    { 1L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "admin@funkoapi.com", null, false, false, null, "$2a$12$FMQKFvn9GqrqkuY8Gwm8J.eo2xq9ZHxiGeoUObpf/c3DL8/5lo2PW", null, "ADMIN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "admin" },
                    { 2L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "user@funkoapi.com", null, false, false, null, "$2a$12$YgFVgrCGk6TyiQiKYBujmOvf.Sx94.9AAZ0X4T7COcDAdtR0HZkGm", null, "USER", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "user" },
                    { 3L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "test@test.com", null, false, false, null, "$2a$12$.q7bnzBb9.r4cpqLyO3tBOlO54ozRPxjp0hv06OWD64A.OomLAYuK", null, "USER", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "testuser" }
                });

            migrationBuilder.InsertData(
                table: "quizzes",
                columns: new[] { "Id", "CreatedAt", "CreatorId", "EsBorrador", "EsPublico", "GameCode", "Likes", "Nombre", "UpdatedAt", "Visitas" },
                values: new object[,]
                {
                    { 1L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1L, false, true, "HIST01", 42, "Trivia de Historia General", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 150 },
                    { 2L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2L, false, true, "CULT02", 23, "Cultura General - Nivel Fácil", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 89 },
                    { 3L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1L, false, true, "SCIN03", 67, "Ciencia y Naturaleza", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 234 },
                    { 4L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 3L, false, true, "GEOM04", 51, "Geografía Mundial", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 178 },
                    { 5L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2L, false, true, "CINE05", 95, "Entretenimiento y Cine", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 312 }
                });

            migrationBuilder.InsertData(
                table: "quizzes",
                columns: new[] { "Id", "CreatedAt", "CreatorId", "EsBorrador", "GameCode", "Nombre", "UpdatedAt" },
                values: new object[] { 6L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1L, false, "PRIV06", "Quiz Privado de Prueba", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_GameHistories_EndedAt",
                table: "GameHistories",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GameHistories_GameId",
                table: "GameHistories",
                column: "GameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameHistories_QuizId",
                table: "GameHistories",
                column: "QuizId");

            migrationBuilder.CreateIndex(
                name: "IX_preguntas_CreatorId",
                table: "preguntas",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_preguntas_QuizId",
                table: "preguntas",
                column: "QuizId");

            migrationBuilder.CreateIndex(
                name: "IX_quizzes_CreatorId",
                table: "quizzes",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_quizzes_GameCode",
                table: "quizzes",
                column: "GameCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_respuestas_PreguntaId",
                table: "respuestas",
                column: "PreguntaId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_GoogleId",
                table: "users",
                column: "GoogleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameHistories");

            migrationBuilder.DropTable(
                name: "respuestas");

            migrationBuilder.DropTable(
                name: "preguntas");

            migrationBuilder.DropTable(
                name: "quizzes");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
