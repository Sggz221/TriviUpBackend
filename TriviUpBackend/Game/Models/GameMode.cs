namespace TriviUpBackend.Game.Models;

/// <summary>
/// Modo de juego elegido al crear la sala.
/// </summary>
public enum GameMode
{
    /// <summary>Cada jugador responde desde su dispositivo.</summary>
    Normal,

    /// <summary>
    /// Partida en persona: los jugadores dicen la respuesta en voz alta y el anfitrión, que ve
    /// la correcta, la marca y la confirma por ellos. Los jugadores solo usan comodines.
    /// </summary>
    Presencial
}
