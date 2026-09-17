using TriviUpBackend.Game.Models;

namespace TriviUpBackend.Game.Services;

/// <summary>
/// Implementación del gestor de turnos.
/// Utiliza una cola para gestionar el orden de los jugadores.
/// </summary>
public class TurnManager : ITurnManager
{
    private Queue<long> _turnOrder = new();

    public void InitializeQueue(IEnumerable<long> playerIds)
    {
        var random = new Random();
        _turnOrder = new Queue<long>(
            playerIds.OrderBy(x => random.Next())
        );
    }

    public long? GetCurrentPlayer()
    {
        return _turnOrder.Count > 0 ? _turnOrder.Peek() : null;
    }

    public long? GetNextPlayer()
    {
        if (_turnOrder.Count == 0) return null;

        var current = _turnOrder.Dequeue();
        _turnOrder.Enqueue(current);
        return current;
    }

    public bool HasPlayers()
    {
        return _turnOrder.Count > 0;
    }

    public void Reset(IEnumerable<long> playerIds)
    {
        InitializeQueue(playerIds);
    }
}
