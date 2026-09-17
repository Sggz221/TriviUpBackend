namespace TriviUpBackend.Game.Persistence;

/// <summary>
/// Almacén distribuido de sesiones de partida (Redis o in-memory).
/// </summary>
public interface IGameSessionStore
{
    Task<GameSessionDocument?> GetAsync(string roomCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea la sesión solo si el código no existe. Devuelve false si el código ya está ocupado.
    /// </summary>
    Task<bool> TryCreateAsync(GameSessionDocument session, CancellationToken cancellationToken = default);

    Task SaveAsync(GameSessionDocument session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina la sesión por completo (p. ej. al cerrar la sala porque el owner la abandonó).
    /// </summary>
    Task RemoveAsync(string roomCode, CancellationToken cancellationToken = default);

    Task MarkFinishedAsync(GameSessionDocument session, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<IAsyncDisposable?> AcquireLockAsync(string roomCode, TimeSpan timeout, CancellationToken cancellationToken = default);

    Task SetConnectionMappingAsync(string connectionId, long userId, string roomCode, CancellationToken cancellationToken = default);

    Task ClearConnectionMappingAsync(string connectionId, CancellationToken cancellationToken = default);

    Task<ConnectionMapping?> GetConnectionMappingAsync(string connectionId, CancellationToken cancellationToken = default);

    Task SetUserRoomAsync(long userId, string roomCode, CancellationToken cancellationToken = default);

    Task ClearUserRoomAsync(long userId, CancellationToken cancellationToken = default);

    Task<string?> GetUserRoomAsync(long userId, CancellationToken cancellationToken = default);

    Task ScheduleDeadlineAsync(string roomCode, long deadlineUnixMs, long turnGeneration, CancellationToken cancellationToken = default);

    Task ClearDeadlineAsync(string roomCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DueDeadline>> GetDueDeadlinesAsync(long nowUnixMs, int limit, CancellationToken cancellationToken = default);
}
