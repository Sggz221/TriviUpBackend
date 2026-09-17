using StackExchange.Redis;
using TriviUpBackend.Game.Configuration;
using TriviUpBackend.Game.Persistence;
using TriviUpBackend.Game.Services;
using TriviUpBackend.Game.Workers;

namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Configuración de Redis para estado de partidas y backplane SignalR.
/// </summary>
public static class RedisConfig
{
    public const string RedisUrlEnvVar = "REDIS_URL";

    public static string? ResolveRedisConnectionString(IConfiguration configuration)
    {
        var redisUrl = Environment.GetEnvironmentVariable(RedisUrlEnvVar);
        if (!string.IsNullOrWhiteSpace(redisUrl))
        {
            return ConvertRedisUrl(redisUrl);
        }

        return configuration.GetConnectionString("Redis");
    }

    /// <summary>
    /// Convierte una URL estilo Railway (redis:// / rediss://) a connection string de StackExchange.Redis.
    /// </summary>
    public static string ConvertRedisUrl(string redisUrl)
    {
        if (!redisUrl.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) &&
            !redisUrl.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            return redisUrl;
        }

        var uri = new Uri(redisUrl);
        var userInfo = uri.UserInfo.Split(':', 2);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) :
            userInfo.Length == 1 && !string.IsNullOrEmpty(userInfo[0]) ? Uri.UnescapeDataString(userInfo[0]) : null;

        var options = new ConfigurationOptions
        {
            EndPoints = { { uri.Host, uri.Port > 0 ? uri.Port : 6379 } },
            AbortOnConnectFail = false,
            Ssl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase)
        };

        if (!string.IsNullOrEmpty(password))
        {
            options.Password = password;
        }

        if (userInfo.Length > 1 && !string.IsNullOrEmpty(userInfo[0]))
        {
            options.User = Uri.UnescapeDataString(userInfo[0]);
        }

        if (uri.AbsolutePath.Length > 1 && int.TryParse(uri.AbsolutePath.TrimStart('/'), out var db))
        {
            options.DefaultDatabase = db;
        }

        return options.ToString();
    }

    public static IServiceCollection AddGameRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<GameOptions>(configuration.GetSection(GameOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GameOptions>>().Value);

        var redisConnectionString = ResolveRedisConnectionString(configuration);
        var useRedis = !string.IsNullOrWhiteSpace(redisConnectionString);

        if (!useRedis && environment.IsProduction())
        {
            throw new InvalidOperationException(
                $"REDIS_URL (o ConnectionStrings:Redis) es obligatorio en producción para estado de partidas y SignalR.");
        }

        if (useRedis)
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisConnectionString!));

            services.AddSignalR()
                .AddStackExchangeRedis(redisConnectionString!, options =>
                {
                    options.Configuration.ChannelPrefix = RedisChannel.Literal("TriviUp");
                });

            services.AddSingleton<IGameSessionStore, RedisGameSessionStore>();
            services.AddHealthChecks()
                .AddCheck<RedisHealthCheck>("redis");
        }
        else
        {
            services.AddSignalR();
            services.AddSingleton<IGameSessionStore, InMemoryGameSessionStore>();
            services.AddHealthChecks();
        }

        services.AddSingleton<IGameService, GameService>();
        services.AddSingleton<ITurnDeadlineProcessor>(sp => (ITurnDeadlineProcessor)sp.GetRequiredService<IGameService>());
        services.AddHostedService<TurnDeadlineWorker>();

        return services;
    }
}
