using System.Collections.Concurrent;
using System.Text.Json;

namespace TriviUpBackend.Game.Persistence;

/// <summary>
/// Implementación in-memory de <see cref="IGameSessionStore"/> para desarrollo y tests.
/// </summary>
public sealed class InMemoryGameSessionStore : IGameSessionStore
{
    private readonly ConcurrentDictionary<string, string> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConnectionMapping> _connections = new();
    private readonly ConcurrentDictionary<long, string> _userRooms = new();
    private readonly ConcurrentDictionary<string, (long DeadlineUnixMs, long TurnGeneration)> _deadlines = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _deadlineSync = new();

    public Task<GameSessionDocument?> GetAsync(string roomCode, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(roomCode, out var json))
        {
            return Task.FromResult<GameSessionDocument?>(null);
        }

        var doc = JsonSerializer.Deserialize<GameSessionDocument>(json, GameSessionMapper.JsonOptions);
        return Task.FromResult(doc);
    }

    public Task<bool> TryCreateAsync(GameSessionDocument session, CancellationToken cancellationToken = default)
    {
        session.Revision = 1;
        var json = JsonSerializer.Serialize(session, GameSessionMapper.JsonOptions);
        return Task.FromResult(_sessions.TryAdd(session.RoomCode, json));
    }

    public Task SaveAsync(GameSessionDocument session, CancellationToken cancellationToken = default)
    {
        session.Revision++;
        var json = JsonSerializer.Serialize(session, GameSessionMapper.JsonOptions);
        _sessions[session.RoomCode] = json;
        return Task.CompletedTask;
    }

    public Task MarkFinishedAsync(GameSessionDocument session, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        session.State = Models.GameState.Finished;
        return SaveAsync(session, cancellationToken);
    }

    public async Task<IAsyncDisposable?> AcquireLockAsync(string roomCode, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var sem = _locks.GetOrAdd(roomCode, _ => new SemaphoreSlim(1, 1));
        var acquired = await sem.WaitAsync(timeout, cancellationToken);
        return acquired ? new Releaser(sem) : null;
    }

    public Task SetConnectionMappingAsync(string connectionId, long userId, string roomCode, CancellationToken cancellationToken = default)
    {
        _connections[connectionId] = new ConnectionMapping(userId, roomCode);
        return Task.CompletedTask;
    }

    public Task ClearConnectionMappingAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        _connections.TryRemove(connectionId, out _);
        return Task.CompletedTask;
    }

    public Task<ConnectionMapping?> GetConnectionMappingAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        _connections.TryGetValue(connectionId, out var mapping);
        return Task.FromResult<ConnectionMapping?>(mapping);
    }

    public Task SetUserRoomAsync(long userId, string roomCode, CancellationToken cancellationToken = default)
    {
        _userRooms[userId] = roomCode;
        return Task.CompletedTask;
    }

    public Task ClearUserRoomAsync(long userId, CancellationToken cancellationToken = default)
    {
        _userRooms.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    public Task<string?> GetUserRoomAsync(long userId, CancellationToken cancellationToken = default)
    {
        _userRooms.TryGetValue(userId, out var room);
        return Task.FromResult(room);
    }

    public Task ScheduleDeadlineAsync(string roomCode, long deadlineUnixMs, long turnGeneration, CancellationToken cancellationToken = default)
    {
        lock (_deadlineSync)
        {
            _deadlines[roomCode] = (deadlineUnixMs, turnGeneration);
        }
        return Task.CompletedTask;
    }

    public Task ClearDeadlineAsync(string roomCode, CancellationToken cancellationToken = default)
    {
        lock (_deadlineSync)
        {
            _deadlines.TryRemove(roomCode, out _);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DueDeadline>> GetDueDeadlinesAsync(long nowUnixMs, int limit, CancellationToken cancellationToken = default)
    {
        lock (_deadlineSync)
        {
            var due = _deadlines
                .Where(kv => kv.Value.DeadlineUnixMs <= nowUnixMs)
                .OrderBy(kv => kv.Value.DeadlineUnixMs)
                .Take(limit)
                .Select(kv => new DueDeadline(kv.Key, kv.Value.DeadlineUnixMs, kv.Value.TurnGeneration))
                .ToList();
            return Task.FromResult<IReadOnlyList<DueDeadline>>(due);
        }
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
