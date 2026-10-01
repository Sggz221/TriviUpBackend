using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TriviUpBackend.Database.Migrations
{
    /// <inheritdoc />
    public partial class QuizPools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PoolsJson",
                table: "quizzes",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 1L,
                column: "PoolsJson",
                value: null);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 2L,
                column: "PoolsJson",
                value: null);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 3L,
                column: "PoolsJson",
                value: null);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 4L,
                column: "PoolsJson",
                value: null);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 5L,
                column: "PoolsJson",
                value: null);

            migrationBuilder.UpdateData(
                table: "quizzes",
                keyColumn: "Id",
                keyValue: 6L,
                column: "PoolsJson",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PoolsJson",
                table: "quizzes");
        }
    }
}
