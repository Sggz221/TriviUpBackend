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
                var connectionString = ResolvePostgresConnectionString(configuration);
                options.UseNpgsql(connectionString);
            }
            else
            {
                options.UseInMemoryDatabase("TriviUpDb");
            }
        });

        return services;
    }

    /// <summary>
    /// Resuelve la connection string de Postgres priorizando URLs públicas de Railway
    /// cuando el host privado (*.railway.internal) no es usable.
    /// </summary>
    internal static string ResolvePostgresConnectionString(IConfiguration configuration)
    {
        var databaseUrl = FirstNonEmpty(
            Environment.GetEnvironmentVariable("DATABASE_URL"),
            configuration["DATABASE_URL"]);

        var publicUrl = FirstNonEmpty(
            Environment.GetEnvironmentVariable("DATABASE_PUBLIC_URL"),
            configuration["DATABASE_PUBLIC_URL"]);

        var forcePublic = IsTruthy(Environment.GetEnvironmentVariable("USE_DATABASE_PUBLIC_URL"));
        var preferPrivate = IsTruthy(Environment.GetEnvironmentVariable("PREFER_PRIVATE_DATABASE"));

        string? chosen = null;
        string source;

        if (forcePublic && !string.IsNullOrWhiteSpace(publicUrl))
        {
            chosen = publicUrl;
            source = "DATABASE_PUBLIC_URL (USE_DATABASE_PUBLIC_URL=true)";
        }
        else if (!preferPrivate
                 && LooksLikeRailwayInternal(databaseUrl)
                 && !string.IsNullOrWhiteSpace(publicUrl))
        {
            // Railway private DNS is IPv6-only and often fails with
            // "Name or service not known" if private networking isn't ready.
            chosen = publicUrl;
            source = "DATABASE_PUBLIC_URL (fallback from *.railway.internal)";
        }
        else if (!string.IsNullOrWhiteSpace(databaseUrl)
                 && !LooksLikeRailwayInternal(databaseUrl))
        {
            chosen = databaseUrl;
            source = "DATABASE_URL";
        }
        else if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            chosen = publicUrl;
            source = "DATABASE_PUBLIC_URL";
        }
        else if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            chosen = databaseUrl;
            source = "DATABASE_URL";
        }
        else if (TryBuildFromPgEnvironment(out var fromPg, out var pgSource))
        {
            chosen = fromPg;
            source = pgSource;
        }
        else
        {
            chosen = configuration.GetConnectionString("DefaultConnection");
            source = "ConnectionStrings:DefaultConnection";
        }

        if (string.IsNullOrWhiteSpace(chosen))
        {
            throw new InvalidOperationException(
                "No se encontró connection string para PostgreSQL. " +
                "Configura DATABASE_PUBLIC_URL (recomendado en Railway), DATABASE_URL, " +
                "variables PG*, o ConnectionStrings:DefaultConnection.");
        }

        var normalized = ConvertPostgresUriToNpgsql(chosen);
        LogConnectionChoice(source, normalized);
        return normalized;
    }

    /// <summary>
    /// Convierte URIs <c>postgres://</c> / <c>postgresql://</c> a formato Npgsql.
    /// </summary>
    internal static string ConvertPostgresUriToNpgsql(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "",
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : ""
        };

        if (!string.IsNullOrEmpty(uri.Query))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = Uri.UnescapeDataString(kv[0]);
                var value = Uri.UnescapeDataString(kv[1]);

                if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                {
                    builder.SslMode = Enum.TryParse<SslMode>(value, ignoreCase: true, out var mode)
                        ? mode
                        : SslMode.Prefer;
                }
                else if (key.Equals("ssl", StringComparison.OrdinalIgnoreCase) &&
                         value.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    builder.SslMode = SslMode.Require;
                }
            }
        }

        // Railway public proxy suele exigir SSL
        if (builder.Host.Contains("proxy.rlwy.net", StringComparison.OrdinalIgnoreCase)
            && builder.SslMode == SslMode.Disable)
        {
            builder.SslMode = SslMode.Require;
        }

        return builder.ToString();
    }

    private static bool TryBuildFromPgEnvironment(out string connectionString, out string source)
    {
        connectionString = "";
        source = "";

        var host = Environment.GetEnvironmentVariable("PGHOST");
        var user = Environment.GetEnvironmentVariable("PGUSER");
        var password = Environment.GetEnvironmentVariable("PGPASSWORD");
        var database = Environment.GetEnvironmentVariable("PGDATABASE");

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(database))
        {
            return false;
        }

        var portRaw = Environment.GetEnvironmentVariable("PGPORT");
        var port = int.TryParse(portRaw, out var parsedPort) ? parsedPort : 5432;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Username = user,
            Password = password ?? "",
            Database = database
        };

        if (LooksLikeRailwayInternalHost(host))
        {
            // Sin SSL típico en red privada
        }
        else if (host.Contains("proxy.rlwy.net", StringComparison.OrdinalIgnoreCase))
        {
            builder.SslMode = SslMode.Require;
        }

        connectionString = builder.ToString();
        source = "PGHOST/PGUSER/PGDATABASE";
        return true;
    }

    private static bool LooksLikeRailwayInternal(string? urlOrHost)
    {
        if (string.IsNullOrWhiteSpace(urlOrHost)) return false;
        if (LooksLikeRailwayInternalHost(urlOrHost)) return true;

        try
        {
            if (urlOrHost.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
            {
                return LooksLikeRailwayInternalHost(new Uri(urlOrHost).Host);
            }
        }
        catch (UriFormatException)
        {
            // Npgsql key=value: buscar Host=
            foreach (var part in urlOrHost.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    return LooksLikeRailwayInternalHost(kv[1].Trim());
                }
            }
        }

        return false;
    }

    private static bool LooksLikeRailwayInternalHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        host.EndsWith(".railway.internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static void LogConnectionChoice(string source, string npgsqlConnectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(npgsqlConnectionString);
            Console.WriteLine(
                $"[DatabaseConfig] Using PostgreSQL source={source}; Host={builder.Host}; Port={builder.Port}; Database={builder.Database}");
        }
        catch
        {
            Console.WriteLine($"[DatabaseConfig] Using PostgreSQL source={source}");
        }
    }
}
