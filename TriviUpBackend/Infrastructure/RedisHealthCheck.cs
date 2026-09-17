using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Health check de conectividad Redis.
/// </summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = multiplexer.GetDatabase();
            var pong = await db.PingAsync();
            return HealthCheckResult.Healthy($"Redis ping {pong.TotalMilliseconds:F0}ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis unreachable", ex);
        }
    }
}
