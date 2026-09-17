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
    int TotalQuestions
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
    bool IsConnected
);

/// <summary>
/// Resultado de un turno (respuesta a una pregunta).
/// </summary>
public record TurnResultDto(
    long PlayerId,
    bool IsCorrect,
    int CorrectAnswerIndex,
    int PointsEarned,
    int NewTotalScore
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
/// Datos del turno iniciado para un jugador.
/// </summary>
public record TurnStartedDto(
    long CurrentPlayerId,
    bool IsMyTurn,
    QuestionDto Question,
    int TimeLimit
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
