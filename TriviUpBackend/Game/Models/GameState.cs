namespace TriviUpBackend.Game.Models;

public enum GameState
{
    Waiting,      // Sala creada, esperando jugadores
    Starting,     // Partida a punto de comenzar (countdown)
    Playing,      // Partida en curso
    Paused,       // Partida pausada
    Finished,     // Partida terminada
    PhaseBreak    // Intermedio entre fases, a la espera de que el anfitrión continúe
}
