using System.Text.Json;
using StackExchange.Redis;
using TriviUpBackend.Game.Models;

namespace TriviUpBackend.Game.Persistence;

/// <summary>
/// Almacén Redis de sesiones de partida con locks, índices y deadlines.
/// </summary>
public sealed class RedisGameSessionStore(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisGameSessionStore> logger) : IGameSessionStore
{
    private const string KeyPrefix = "triviup:";
    private static readonly LuaScript ReleaseLockScript = LuaScript.Prepare(@"
        if redis.call('get', @key) == @token then
            return redis.call('del', @key)
        else
            return 0
        end");

    private readonly IDatabase _db = multiplexer.GetDatabase();

    private static string SessionKey(string roomCode) => $"{KeyPrefix}room:{roomCode}";
    private static string LockKey(string roomCode) => $"{KeyPrefix}lock:room:{roomCode}";
    private static string ConnKey(string connectionId) => $"{KeyPrefix}conn:{connectionId}";
    private static string UserRoomKey(long userId) => $"{KeyPrefix}user:{userId}:room";
    private static string DeadlineZSetKey => $"{KeyPrefix}turn-deadlines";
    private static string DeadlineMetaKey(string roomCode) => $"{KeyPrefix}deadline-meta:{roomCode}";

    public async Task<GameSessionDocument?> GetAsync(string roomCode, CancellationToken cancellationToken = default)
    {
        var value = await _db.StringGetAsync(SessionKey(roomCode));
        if (value.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<GameSessionDocument>((string)value!, GameSessionMapper.JsonOptions);
    }

    public async Task<bool> TryCreateAsync(GameSessionDocument session, CancellationToken cancellationToken = default)
    {
        session.Revision = 1;
        var json = JsonSerializer.Serialize(session, GameSessionMapper.JsonOptions);
        return await _db.StringSetAsync(SessionKey(session.RoomCode), json, when: When.NotExists);
    }

    public async Task SaveAsync(GameSessionDocument session, CancellationToken cancellationToken = default)
    {
        session.Revision++;
        var json = JsonSerializer.Serialize(session, GameSessionMapper.JsonOptions);
        await _db.StringSetAsync(SessionKey(session.RoomCode), json);
    }

    public async Task RemoveAsync(string roomCode, CancellationToken cancellationToken = default)
    {
        await _db.KeyDeleteAsync(SessionKey(roomCode));
    }

    public async Task MarkFinishedAsync(GameSessionDocument session, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        session.State = GameState.Finished;
        session.Revision++;
        var json = JsonSerializer.Serialize(session, GameSessionMapper.JsonOptions);
        await _db.StringSetAsync(SessionKey(session.RoomCode), json, ttl);
        await ClearDeadlineAsync(session.RoomCode, cancellationToken);
    }

    public async Task<IAsyncDisposable?> AcquireLockAsync(string roomCode, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var token = Guid.NewGuid().ToString("N");
        var key = LockKey(roomCode);
        var expiry = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : timeout;
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _db.StringSetAsync(key, token, expiry, When.NotExists))
            {
                return new RedisLock(_db, key, token, logger);
            }

            await Task.Delay(25, cancellationToken);
        }

        logger.LogWarning("Failed to acquire lock for room {RoomCode}", roomCode);
        return null;
    }

    public async Task SetConnectionMappingAsync(string connectionId, long userId, string roomCode, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new ConnectionMapping(userId, roomCode), GameSessionMapper.JsonOptions);
        await _db.StringSetAsync(ConnKey(connectionId), payload, TimeSpan.FromHours(12));
    }

    public async Task ClearConnectionMappingAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        await _db.KeyDeleteAsync(ConnKey(connectionId));
    }

    public async Task<ConnectionMapping?> GetConnectionMappingAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        var value = await _db.StringGetAsync(ConnKey(connectionId));
        if (value.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<ConnectionMapping>((string)value!, GameSessionMapper.JsonOptions);
    }

    public async Task SetUserRoomAsync(long userId, string roomCode, CancellationToken cancellationToken = default)
    {
        await _db.StringSetAsync(UserRoomKey(userId), roomCode, TimeSpan.FromHours(12));
    }

    public async Task ClearUserRoomAsync(long userId, CancellationToken cancellationToken = default)
    {
        await _db.KeyDeleteAsync(UserRoomKey(userId));
    }

    public async Task<string?> GetUserRoomAsync(long userId, CancellationToken cancellationToken = default)
    {
        var value = await _db.StringGetAsync(UserRoomKey(userId));
        return value.IsNullOrEmpty ? null : (string?)value;
    }

    public async Task ScheduleDeadlineAsync(string roomCode, long deadlineUnixMs, long turnGeneration, CancellationToken cancellationToken = default)
    {
        await _db.StringSetAsync(DeadlineMetaKey(roomCode), turnGeneration.ToString());
        await _db.SortedSetAddAsync(DeadlineZSetKey, roomCode, deadlineUnixMs);
    }

    public async Task ClearDeadlineAsync(string roomCode, CancellationToken cancellationToken = default)
    {
        await _db.SortedSetRemoveAsync(DeadlineZSetKey, roomCode);
        await _db.KeyDeleteAsync(DeadlineMetaKey(roomCode));
    }

    public async Task<IReadOnlyList<DueDeadline>> GetDueDeadlinesAsync(long nowUnixMs, int limit, CancellationToken cancellationToken = default)
    {
        var entries = await _db.SortedSetRangeByScoreWithScoresAsync(
            DeadlineZSetKey,
            double.NegativeInfinity,
            nowUnixMs,
            Exclude.None,
            Order.Ascending,
            0,
            limit);

        var result = new List<DueDeadline>(entries.Length);
        foreach (var entry in entries)
        {
            var roomCode = (string)entry.Element!;
            var genValue = await _db.StringGetAsync(DeadlineMetaKey(roomCode));
            long.TryParse(genValue, out var generation);
            result.Add(new DueDeadline(roomCode, (long)entry.Score, generation));
        }

        return result;
    }

    private sealed class RedisLock(IDatabase db, string key, string token, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.ScriptEvaluateAsync(ReleaseLockScript, new { key = (RedisKey)key, token = (RedisValue)token });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to release Redis lock {Key}", key);
            }
        }
    }
}
