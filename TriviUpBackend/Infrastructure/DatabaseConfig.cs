using Microsoft.EntityFrameworkCore;
using Npgsql;
using TriviUpBackend.Database;

namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Configuración de la base de datos.
/// Registra el DbContext con soporte para PostgreSQL o InMemory.
/// </summary>
public static class DatabaseConfig
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<Context>(options =>
        {
            var provider = configuration["Database:Provider"] ?? "InMemory";

            if (provider == "PostgreSQL")
            {
                // Intentar leer DATABASE_URL del entorno (inyectada por Railway/Postgres plugin)
                // Si no existe, usar GetConnectionString (para desarrollo local)
                var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
                    ?? configuration.GetConnectionString("DefaultConnection")
                    ?? throw new InvalidOperationException("No se encontró connection string para PostgreSQL. Configura DATABASE_URL o DefaultConnection.");

                // Si la connection string es un URI de Railway (postgresql://...), convertirla
                if (connectionString.StartsWith("postgresql://"))
                {
                    var uri = new Uri(connectionString);
                    var builder = new NpgsqlConnectionStringBuilder
                    {
                        Host = uri.Host,
                        Port = uri.Port,
                        Database = uri.AbsolutePath.TrimStart('/'),
                        Username = uri.UserInfo.Split(':')[0],
                        Password = uri.UserInfo.Split(':')[1]
                    };
                    connectionString = builder.ToString();
                }

                options.UseNpgsql(connectionString);
            }
            else
            {
                options.UseInMemoryDatabase("TriviUpDb");
            }
        });

        return services;
    }
}