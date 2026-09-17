namespace TriviUpBackend.Game.Services;

/// <summary>
/// Interfaz para el gestor de turnos de una partida.
/// Controla el orden de los jugadores y el turno actual.
/// </summary>
public interface ITurnManager
{
    /// <summary>
    /// Inicializa la cola de turnos con los IDs de los jugadores.
    /// El orden es aleatorio.
    /// </summary>
    /// <param name="playerIds">Colección de IDs de jugadores.</param>
    void InitializeQueue(IEnumerable<long> playerIds);

    /// <summary>
    /// Obtiene el ID del jugador cuyo turno es actualmente.
    /// </summary>
    /// <returns>ID del jugador actual o null si no hay nadie.</returns>
    long? GetCurrentPlayer();

    /// <summary>
    /// Obtiene el ID del siguiente jugador y rota la cola.
    /// </summary>
    /// <returns>ID del siguiente jugador o null si no hay nadie.</returns>
    long? GetNextPlayer();

    /// <summary>
    /// Indica si hay jugadores en la cola de turnos.
    /// </summary>
    /// <returns>True si hay jugadores, false en caso contrario.</returns>
    bool HasPlayers();

    /// <summary>
    /// Reinicia la cola de turnos con nuevos jugadores.
    /// </summary>
    /// <param name="playerIds">Nuevos IDs de jugadores.</param>
    void Reset(IEnumerable<long> playerIds);
}
