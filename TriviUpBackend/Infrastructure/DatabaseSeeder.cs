using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Database;

namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Prepara la base de datos al arrancar.
/// En PostgreSQL aplica las migraciones de EF Core; con la BD en memoria crea el esquema directamente.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static class DatabaseSeeder
{
    public static void SeedDatabase(this WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Context>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Estamos inicializando la Base de Datos...");

            if (context.Database.IsNpgsql())
            {
                EnsureMigrationsBaseline(context, logger);
                context.Database.Migrate();
            }
            else
            {
                // Base de datos en memoria (desarrollo/tests): no hay migraciones
                context.Database.EnsureCreated();
            }

            logger.LogInformation("Hemos terminado de preparar la Base de Datos.");
        }
    }

    /// <summary>
    /// Las bases de datos creadas antes de usar migraciones (EnsureCreated) ya tienen las tablas
    /// pero no la tabla de historial. Se marca la migración inicial como aplicada para que
    /// Migrate() solo ejecute las migraciones posteriores. En una base nueva no hace nada.
    /// </summary>
    private static void EnsureMigrationsBaseline(Context context, ILogger logger)
    {
        var baseline = context.Database.GetMigrations().FirstOrDefault();
        if (baseline is null) return;

        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            )
            """);

        var historyIsEmpty = context.Database
            .SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "__EFMigrationsHistory" """)
            .AsEnumerable().First() == 0;
        var schemaExists = context.Database
            .SqlQueryRaw<bool>("""SELECT (to_regclass('public.quizzes') IS NOT NULL) AS "Value" """)
            .AsEnumerable().First();

        if (!historyIsEmpty || !schemaExists) return;

        logger.LogWarning("Base de datos anterior a las migraciones: marcando {Baseline} como aplicada", baseline);

        // La columna EsBorrador se añadió antes con un ALTER manual; por si esa base no la tuviera aún
        context.Database.ExecuteSqlRaw(
            """ALTER TABLE quizzes ADD COLUMN IF NOT EXISTS "EsBorrador" boolean NOT NULL DEFAULT FALSE""");

        context.Database.ExecuteSql($"""
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ({baseline}, '9.0.0')
            ON CONFLICT ("MigrationId") DO NOTHING
            """);
    }
}
