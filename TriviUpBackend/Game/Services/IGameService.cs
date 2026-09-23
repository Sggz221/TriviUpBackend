using CSharpFunctionalExtensions;
using TriviUpBackend.Game.Models;
using TriviUpBackend.Game.DTOs;

namespace TriviUpBackend.Game.Services;

/// <summary>
/// Interfaz para el servicio de gestión de partidas.
/// Controla la creación, unión, inicio y gestión de salas de juego.
/// </summary>
public interface IGameService
{
    /// <summary>
    /// Crea una nueva sala de juego para un quiz.
    /// </summary>
    /// <param name="quizId">ID del quiz a jugar.</param>
    /// <param name="ownerId">ID del usuario que crea la sala.</param>
    /// <param name="username">Nombre de usuario del propietario.</param>
    /// <param name="connectionId">ID de conexión SignalR del propietario.</param>
    /// <param name="mode">Modo de juego; el presencial siempre es sin tiempo.</param>
    /// <returns>Código de la sala creada.</returns>
    Task<string> CreateGameAsync(long quizId, long ownerId, string username, string connectionId, int? turnTimeLimitSeconds = null, GameMode mode = GameMode.Normal);

    /// <summary>
    /// Une a un jugador a una sala existente.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="userId">ID del usuario.</param>
    /// <param name="username">Nombre de usuario.</param>
    /// <param name="connectionId">ID de conexión SignalR.</param>
    /// <returns>Resultado con la sala o error.</returns>
    Task<Result<GameRoom>> JoinGameAsync(string roomCode, long userId, string username, string connectionId);

    /// <summary>
    /// Elimina a un jugador de la sala.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="userId">ID del usuario.</param>
    /// <param name="isExplicitLeave">
    /// true si el jugador pulsó "Salir de la Sala" explícitamente (no una desconexión de socket).
    /// Si es el owner y la sala está en espera, esto cierra la sala para todos en vez de
    /// transferir el ownership.
    /// </param>
    /// <returns>true si la sala se cerró como consecuencia de este leave.</returns>
    Task<bool> LeaveGameAsync(string roomCode, long userId, bool isExplicitLeave = false);

    /// <summary>
    /// Inicia la partida. Solo el propietario puede iniciar.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="userId">ID del usuario que intenta iniciar.</param>
    /// <returns>Sala con el estado actualizado o null si no se pudo iniciar.</returns>
    Task<GameRoom?> StartGameAsync(string roomCode, long userId);

    /// <summary>
    /// Envía la respuesta de un jugador a la pregunta actual.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="userId">ID del usuario.</param>
    /// <param name="questionId">ID de la pregunta.</param>
    /// <param name="answerIndex">Índice de la respuesta seleccionada.</param>
    /// <returns>Resultado del turno o null si no es válido.</returns>
    Task<TurnResultDto?> SubmitAnswerAsync(string roomCode, long userId, long questionId, int answerIndex);

    /// <summary>
    /// Usa un comodín del jugador sobre la pregunta actual. Los de turno (Ruleta, Doble o nada)
    /// solo los puede usar quien responde; los de fuera de turno (Robo, Apuesta), el resto.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="userId">ID del jugador.</param>
    /// <param name="tipo">Comodín a usar.</param>
    /// <param name="questionId">Pregunta sobre la que se usa (descarta clics tardíos).</param>
    /// <param name="predictsCorrect">Solo Apuesta: true si apuesta a que el jugador en turno acierta.</param>
    Task<Result<ComodinUsedDto>> UseComodinAsync(string roomCode, long userId, ComodinTipo tipo, long questionId, bool? predictsCorrect = null);

    /// <summary>
    /// Modo presencial: el anfitrión marca (o desmarca con null) la opción que ha dicho quien
    /// responde. No puntúa: la marca se difunde a la sala y se puede cambiar hasta confirmarla.
    /// </summary>
    Task<Result> MarkAnswerAsync(string roomCode, long ownerId, long questionId, int? answerIndex);

    /// <summary>
    /// Modo presencial: el anfitrión confirma la opción marcada. Se puntúa a quien responde y el
    /// resultado queda en pantalla hasta que el anfitrión pase de pregunta.
    /// </summary>
    Task<Result<TurnResultDto>> ConfirmAnswerAsync(string roomCode, long ownerId, long questionId);

    /// <summary>
    /// Modo presencial: el anfitrión pasa a la siguiente pregunta tras ver el resultado.
    /// </summary>
    Task<Result> NextQuestionAsync(string roomCode, long ownerId);

    /// <summary>
    /// Pausa la partida. Solo el propietario puede pausar.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="userId">ID del usuario que intenta pausar.</param>
    /// <returns>Resultado con éxito o error.</returns>
    Task<Result> PauseGameAsync(string roomCode, long userId);

    /// <summary>
    /// Reanuda la partida pausada. Solo el propietario puede reanudar.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="userId">ID del usuario que intenta reanudar.</param>
    /// <returns>Resultado con éxito o error.</returns>
    Task<Result> ResumeGameAsync(string roomCode, long userId);

    /// <summary>
    /// Sale del intermedio entre fases y arranca el primer turno de la siguiente. Solo el propietario.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="userId">ID del usuario que intenta continuar.</param>
    Task<Result> ContinuePhaseAsync(string roomCode, long userId);

    /// <summary>
    /// Devuelve el estado actual (pregunta, turno, pausa) de una partida en curso para quien
    /// se reconecta a la sala, o null si la partida no está en curso.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    Task<RejoinStateDto?> GetRejoinStateAsync(string roomCode);

    /// <summary>
    /// Expulsa a un jugador de la sala. Solo el propietario puede expulsar.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="ownerId">ID del propietario.</param>
    /// <param name="playerIdToKick">ID del jugador a expulsar.</param>
    /// <returns>Resultado con el connectionId del jugador expulsado o error.</returns>
    Task<Result<string?>> KickPlayerAsync(string roomCode, long ownerId, long playerIdToKick);

    /// <summary>
    /// Marca o desmarca a un jugador como espectador. Solo el propietario y solo en el lobby.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="ownerId">ID del propietario.</param>
    /// <param name="targetUserId">ID del jugador afectado.</param>
    /// <param name="isSpectator">true para convertirlo en espectador, false para devolverlo a jugador.</param>
    Task<Result> SetSpectatorAsync(string roomCode, long ownerId, long targetUserId, bool isSpectator);

    /// <summary>
    /// Maneja la desconexión de un jugador.
    /// </summary>
    /// <param name="connectionId">ID de conexión SignalR.</param>
    Task HandleDisconnectionAsync(string connectionId);

    /// <summary>
    /// Obtiene el código de sala asociado a una conexión.
    /// </summary>
    /// <param name="connectionId">ID de conexión SignalR.</param>
    /// <returns>Código de sala o null si no existe.</returns>
    Task<string?> GetRoomCodeByConnectionAsync(string connectionId);
}
