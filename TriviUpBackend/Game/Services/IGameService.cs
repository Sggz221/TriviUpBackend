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
    /// <returns>Código de la sala creada.</returns>
    Task<string> CreateGameAsync(long quizId, long ownerId, string username, string connectionId);

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
    Task LeaveGameAsync(string roomCode, long userId);

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
    /// <param name="timeRemaining">Tiempo restante en segundos.</param>
    /// <returns>Resultado del turno o null si no es válido.</returns>
    Task<TurnResultDto?> SubmitAnswerAsync(string roomCode, long userId, long questionId, int answerIndex, int timeRemaining);

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
    /// Expulsa a un jugador de la sala. Solo el propietario puede expulsar.
    /// </summary>
    /// <param name="roomCode">Código de la sala.</param>
    /// <param name="ownerId">ID del propietario.</param>
    /// <param name="playerIdToKick">ID del jugador a expulsar.</param>
    /// <returns>Resultado con el connectionId del jugador expulsado o error.</returns>
    Task<Result<string?>> KickPlayerAsync(string roomCode, long ownerId, long playerIdToKick);

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
