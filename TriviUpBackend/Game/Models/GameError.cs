namespace TriviUpBackend.Game.Models;

/// <summary>
/// Errores posibles durante una partida de juego.
/// </summary>
public enum GameError
{
    /// <summary>Sala no encontrada.</summary>
    RoomNotFound,
    /// <summary>Sala llena, no hay espacio para más jugadores.</summary>
    RoomFull,
    /// <summary>El jugador ya se ha unido a la sala.</summary>
    AlreadyJoined,
    /// <summary>El usuario no es el propietario de la sala.</summary>
    NotOwner,
    /// <summary>No es el turno del jugador.</summary>
    NotYourTurn,
    /// <summary>La partida ya ha comenzado.</summary>
    GameAlreadyStarted,
    /// <summary>La partida no ha comenzado aún.</summary>
    GameNotStarted,
    /// <summary>La respuesta proporcionada no es válida.</summary>
    InvalidAnswer,
    /// <summary>Conexión perdida con el jugador.</summary>
    ConnectionLost,
    /// <summary>No hay suficientes jugadores para iniciar.</summary>
    MinPlayersNotReached
}
