using TriviUpBackend.Game.Models;

namespace TriviUpBackend.Game.DTOs;

/// <summary>
/// Solicitud para crear una nueva sala de juego.
/// </summary>
public record CreateGameRequest(
    long QuizId
);

/// <summary>
/// Solicitud para unirse a una sala de juego.
/// </summary>
public record JoinGameRequest(
    string RoomCode
);

/// <summary>
/// Envío de respuesta por parte de un jugador.
/// </summary>
public record AnswerSubmission(
    string RoomCode,
    long QuestionId,
    int AnswerIndex,
    int TimeRemaining
);

/// <summary>
/// Estado actual del juego.
/// </summary>
public record GameStateDto(
    string RoomCode,
    string State,
    List<PlayerDto> Players,
    int CurrentQuestionIndex,
    int TotalQuestions,
    string Mode = "Normal"
);

/// <summary>
/// Información de un jugador en la sala.
/// </summary>
public record PlayerDto(
    long UserId,
    string Username,
    int Score,
    int CorrectAnswers,
    int WrongAnswers,
    bool IsCurrentTurn,
    bool IsOwner,
    bool IsConnected,
    bool IsSpectator = false,
    List<string>? AvailableComodines = null,
    Dictionary<string, int>? RemainingUses = null,
    Dictionary<string, int>? MaxUses = null
);

/// <summary>
/// Resultado de un turno (respuesta a una pregunta).
/// </summary>
public record TurnResultDto(
    long PlayerId,
    bool IsCorrect,
    int CorrectAnswerIndex,
    int PointsEarned,
    int NewTotalScore,
    bool IsSteal = false,
    bool DoubleOrNothing = false,
    long? ReturnsToPlayerId = null,
    List<BetResultDto>? Bets = null,
    /// <summary>true si el jugador pasó la pregunta con el comodín Pasar (sin acierto ni fallo).</summary>
    bool Passed = false
);

/// <summary>
/// Resultado de una apuesta al resolverse la pregunta. <c>Refunded</c> = anulada (robo acertado) y comodín devuelto.
/// </summary>
public record BetResultDto(
    long UserId,
    bool PredictsCorrect,
    bool Won,
    bool Refunded,
    int PointsEarned,
    int NewTotalScore
);

/// <summary>
/// Apuesta pública hecha sobre el jugador en turno.
/// </summary>
public record BetDto(
    long UserId,
    bool PredictsCorrect
);

/// <summary>
/// Uso de un comodín, difundido a toda la sala.
/// </summary>
public record ComodinUsedDto(
    long UserId,
    string Username,
    string Tipo,
    long QuestionId,
    List<string> AvailableComodines,
    List<int>? EliminatedAnswerIndexes = null,
    int? RuletaResultado = null,
    bool? PredictsCorrect = null,
    long? StolenFromPlayerId = null,
    int? RuletaHueco = null,
    int? RuletaDuracionMs = null,
    Dictionary<string, int>? RemainingUses = null,
    /// <summary>Jugador afectado por el comodín (Ocultar texto: quien responde).</summary>
    long? TargetPlayerId = null
);

/// <summary>
/// Resultado final de la partida.
/// </summary>
public record GameResultDto(
    string RoomCode,
    string QuizTitle,
    List<PlayerResultDto> PlayerResults,
    int TotalQuestions,
    TimeSpan GameDuration
);

/// <summary>
/// Resultado individual de un jugador en la partida.
/// </summary>
public record PlayerResultDto(
    long UserId,
    string Username,
    int Rank,
    int FinalScore,
    int CorrectAnswers,
    int WrongAnswers,
    int CorrectPercentage
);

/// <summary>
/// Estado de una partida en curso que se reenvía a quien se reconecta a la sala.
/// </summary>
public record RejoinStateDto(
    GameStateDto GameState,
    TurnStartedDto? Turn,
    bool Paused,
    PhaseCompletedDto? PhaseBreak = null,
    HostQuestionInfoDto? HostInfo = null,
    TurnResultDto? LastTurnResult = null
);

/// <summary>
/// Datos del turno iniciado para un jugador.
/// </summary>
public record TurnStartedDto(
    long CurrentPlayerId,
    bool IsMyTurn,
    QuestionDto Question,
    int TimeLimit,
    int FaseNumero = 1,
    string? FaseNombre = null,
    int TotalFases = 1,
    string? FaseColor = null,
    long? TurnOwnerId = null,
    bool IsSteal = false,
    List<int>? EliminatedAnswerIndexes = null,
    List<long>? DoubleOrNothingPlayers = null,
    List<BetDto>? Bets = null,
    long? StolenById = null,
    string Mode = "Normal",
    int? MarkedAnswerIndex = null,
    bool ComodinUsed = false,
    bool CallActive = false,
    bool IsDynamic = false,
    bool BuzzerOpen = false,
    /// <summary>Jugador al que se le oculta el texto de las respuestas en esta pregunta (null = nadie).</summary>
    long? TextHiddenForPlayerId = null,
    /// <summary>Pregunta de colores (IsDynamic también es true: no tiene turno).</summary>
    bool IsColor = false,
    /// <summary>Prueba de colores abierta: todos imitan <see cref="ColorTarget"/> (CurrentPlayerId = 0).</summary>
    bool ColorOpen = false,
    ColorHsb? ColorTarget = null,
    /// <summary>Jugadores que ya han enviado su color (sin revelar cuál, hasta que se resuelve).</summary>
    List<long>? ColorSubmittedPlayerIds = null
);

/// <summary>Pregunta de colores: alguien ha enviado su color (no se revela cuál hasta el final).</summary>
public record ColorSubmittedDto(
    long QuestionId,
    long PlayerId
);

/// <summary>Color enviado por un jugador y su parecido con el objetivo (0-100).</summary>
public record ColorGuessDto(
    long PlayerId,
    string Username,
    ColorHsb Color,
    double Similarity
);

/// <summary>
/// Resultado de la prueba de colores: los colores de todos, ordenados de más a menos parecido, y el ganador
/// (null si nadie envió color: la pregunta se pasa sin puntos).
/// </summary>
public record ColorChallengeResultDto(
    long QuestionId,
    ColorHsb Target,
    List<ColorGuessDto> Guesses,
    long? WinnerId,
    string? WinnerUsername,
    /// <summary>Hubo empate en el mejor parecido y el ganador se eligió al azar.</summary>
    bool TieBroken
);

/// <summary>
/// Ronda dinámica: el equipo que ha pulsado primero y responde la pregunta.
/// </summary>
public record BuzzerWonDto(
    long QuestionId,
    long PlayerId,
    string Username
);

/// <summary>
/// Modo presencial: el anfitrión quitó el cartel de la Llamada.
/// </summary>
public record CallDismissedDto(
    long QuestionId
);

/// <summary>
/// Modo presencial: opción marcada por el anfitrión (null = desmarcada), difundida a toda la sala.
/// </summary>
public record AnswerMarkedDto(
    long QuestionId,
    int? AnswerIndex
);

/// <summary>
/// Datos de la pregunta en curso que solo se envían al anfitrión, nunca al grupo:
/// la respuesta correcta (solo en modo presencial; null en otro caso) y la curiosidad.
/// </summary>
public record HostQuestionInfoDto(
    long QuestionId,
    int? CorrectAnswerIndex,
    string? Curiosidad = null
);

/// <summary>
/// Intermedio al terminar una fase: marcador actual y datos de la fase siguiente.
/// </summary>
public record PhaseCompletedDto(
    string RoomCode,
    int FaseNumero,
    string? FaseNombre,
    string? SiguienteFaseNombre,
    int TotalFases,
    List<PlayerDto> Players,
    string? FaseColor = null,
    int SiguienteFaseNumero = 0,
    string? SiguienteFaseColor = null
);

/// <summary>
/// Datos de una pregunta para enviar al jugador.
/// </summary>
public record QuestionDto(
    long Id,
    string Text,
    List<string> Options,
    string? ImageUrl
);

/// <summary>
/// Historial de una partida jugado.
/// </summary>
public record GameHistoryDto(
    long GameId,
    long QuizId,
    string QuizTitle,
    DateTime StartedAt,
    DateTime EndedAt,
    long OwnerId,
    List<HistoryPlayerResultDto> PlayerResults
);

/// <summary>
/// Resultado de un jugador en el historial.
/// </summary>
public record HistoryPlayerResultDto(
    long UserId,
    string Username,
    int FinalScore,
    int CorrectAnswers,
    int WrongAnswers,
    int Rank
);

/// <summary>
/// Datos cuando el juego es pausado.
/// </summary>
public record GamePausedDto(
    string RoomCode,
    DateTime PausedAt
);

/// <summary>
/// Datos cuando el juego es reanudado.
/// </summary>
public record GameResumedDto(
    string RoomCode,
    int TimeRemaining
);

/// <summary>
/// Datos cuando la sala se cierra (p. ej. el owner la abandona mientras está en espera).
/// </summary>
public record RoomClosedDto(
    string RoomCode,
    string Reason
);

/// <summary>
/// Estadísticas generales del sistema para administradores.
/// </summary>
public record AdminStatsDto(
    int TotalGamesPlayed,
    int TotalQuizzes,
    int TotalUsers,
    int ActiveUsersLast24h,
    QuizWithMostFavoritesDto? MostFavoritesQuiz,
    QuizWithMostVisitsDto? MostVisitsQuiz,
    List<DailyGamesDto> GamesPerDay,
    List<ActiveUsersDto> ActiveUsersPerDay
);

/// <summary>
/// Quiz con más favoritos.
/// </summary>
public record QuizWithMostFavoritesDto(
    long Id,
    string Nombre,
    int Favorites
);

/// <summary>
/// Quiz con más visitas.
/// </summary>
public record QuizWithMostVisitsDto(
    long Id,
    string Nombre,
    int Visitas
);

/// <summary>
/// Juegos jugados por día.
/// </summary>
public record DailyGamesDto(
    DateTime Date,
    int Count
);

/// <summary>
/// Usuarios activos por día.
/// </summary>
public record ActiveUsersDto(
    DateTime Date,
    int Count
);
