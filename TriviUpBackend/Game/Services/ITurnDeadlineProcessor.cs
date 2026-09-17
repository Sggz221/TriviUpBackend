namespace TriviUpBackend.Game.Services;

/// <summary>
/// Procesa timeouts de turno de forma idempotente (usado por el worker de deadlines).
/// </summary>
public interface ITurnDeadlineProcessor
{
    Task ProcessDueTimeoutAsync(string roomCode, long expectedTurnGeneration);
}
