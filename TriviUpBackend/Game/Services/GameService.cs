using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.SignalR;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Game.Configuration;
using TriviUpBackend.Game.DTOs;
using TriviUpBackend.Game.Hubs;
using TriviUpBackend.Game.Models;
using TriviUpBackend.Game.Persistence;
using TriviUpBackend.Game.Repositories;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;

namespace TriviUpBackend.Game.Services;

/// <summary>
/// Servicio de gestión de partidas con estado compartido en <see cref="IGameSessionStore"/>.
/// </summary>
public class GameService : IGameService, ITurnDeadlineProcessor
{
    private readonly IGameSessionStore _store;
    private readonly GameOptions _options;
    private readonly ILogger<GameService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public GameService(
        IGameSessionStore store,
        GameOptions options,
        ILogger<GameService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _store = store;
        _options = options;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<string> CreateGameAsync(long quizId, long ownerId, string username, string connectionId, int? turnTimeLimitSeconds = null, GameMode mode = GameMode.Normal, IReadOnlyDictionary<string, int>? comodines = null)
    {
        _logger.LogInformation("Creating game room for quiz {QuizId} by owner {OwnerId} ({Username})", quizId, ownerId, username);

        using var scope = _scopeFactory.CreateScope();
        var quizRepository = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var quiz = await quizRepository.FindByIdAsync(quizId);
        if (quiz is { EsBorrador: true })
        {
            throw new InvalidOperationException("No se puede jugar un borrador.");
        }
        var quizTitle = quiz?.Nombre ?? "Quiz Desconocido";

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var roomCode = GenerateRoomCodeCandidate();
            var session = new GameSessionDocument
            {
                RoomCode = roomCode,
                QuizId = quizId,
                QuizTitle = quizTitle,
                OwnerId = ownerId,
                // En persona el anfitrión marca y confirma a su ritmo: nunca hay tiempo por turno.
                TurnTimeLimitSeconds = mode == GameMode.Presencial ? 0 : NormalizeTurnTimeLimit(turnTimeLimitSeconds),
                Mode = mode,
                ComodinUsos = ComodinReglas.Normalizar(mode, comodines),
                State = GameState.Waiting,
                Players =
                [
                    new PlayerDocument
                    {
                        UserId = ownerId,
                        Username = username,
                        ConnectionId = connectionId,
                        IsConnected = true,
                        IsOwner = true
                    }
                ]
            };

            if (!await _store.TryCreateAsync(session))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(connectionId))
            {
                await _store.SetConnectionMappingAsync(connectionId, ownerId, roomCode);
            }

            await _store.SetUserRoomAsync(ownerId, roomCode);

            _logger.LogInformation("Game room created: {RoomCode} for quiz {QuizId} by owner {OwnerId}", roomCode, quizId, ownerId);
            return roomCode;
        }

        throw new InvalidOperationException("Unable to allocate a unique room code.");
    }

    /// <inheritdoc />
    public async Task<Result<GameRoom>> JoinGameAsync(string roomCode, long userId, string username, string connectionId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null)
        {
            return Result.Failure<GameRoom>("Room is busy. Please retry.");
        }

        var session = await _store.GetAsync(roomCode);
        if (session is null)
        {
            _logger.LogWarning("Join attempt to non-existent room: {RoomCode}", roomCode);
            return Result.Failure<GameRoom>("Room not found.");
        }

        var ownerCheck = EnsureOwnerActive(session);
        if (ownerCheck == OwnerCheckResult.MustClose)
        {
            await CloseRoomAsync(session, "NO_PLAYERS");
            return Result.Failure<GameRoom>("Room not found.");
        }
        var ownerChangedBeforeJoin = ownerCheck == OwnerCheckResult.Transferred;

        // Un jugador que ya está en la sala puede reconectar en cualquier estado
        // (Waiting, Playing, Paused) — solo un join genuinamente nuevo exige que la
        // sala siga en espera y tenga hueco.
        var existingPlayer = session.Players.FirstOrDefault(p => p.UserId == userId);

        // Jugador anónimo que perdió su id local (otro navegador, datos borrados, caducidad):
        // con la partida en curso lo reconocemos por su nombre, siempre que ese jugador
        // esté desconectado, y reutilizamos su id original.
        if (existingPlayer is null && session.State != GameState.Waiting)
        {
            existingPlayer = session.Players.FirstOrDefault(p =>
                !p.IsConnected && !p.IsOwner &&
                string.Equals(p.Username, username, StringComparison.OrdinalIgnoreCase));
            if (existingPlayer is not null)
            {
                userId = existingPlayer.UserId;
            }
        }

        if (existingPlayer is null)
        {
            if (session.State != GameState.Waiting)
            {
                _logger.LogWarning("Join attempt to non-waiting room: {RoomCode}", roomCode);
                return Result.Failure<GameRoom>("Game has already started.");
            }

            if (session.Players.Count >= _options.MaxPlayersPerRoom)
            {
                _logger.LogWarning("Join attempt to full room: {RoomCode}", roomCode);
                return Result.Failure<GameRoom>("Room is full.");
            }

            session.Players.Add(new PlayerDocument
            {
                UserId = userId,
                Username = username,
                ConnectionId = connectionId,
                IsConnected = true,
                IsOwner = false
            });
        }
        else
        {
            if (!string.IsNullOrEmpty(existingPlayer.ConnectionId) &&
                !string.Equals(existingPlayer.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                await _store.ClearConnectionMappingAsync(existingPlayer.ConnectionId);
            }

            existingPlayer.IsConnected = true;
            existingPlayer.DisconnectedAt = null;
            existingPlayer.ConnectionId = connectionId;
            existingPlayer.Username = username;
        }

        await _store.SaveAsync(session);
        await _store.SetConnectionMappingAsync(connectionId, userId, roomCode);
        await _store.SetUserRoomAsync(userId, roomCode);

        if (ownerChangedBeforeJoin)
        {
            // El resto de la sala no se había enterado todavía de este cambio
            // (solo lo ve quien se está uniendo ahora mismo, vía PlayersList al caller).
            await BroadcastPlayersListAsync(session);
        }

        _logger.LogInformation("User {UserId} ({Username}) joined room {RoomCode}", userId, username, roomCode);
        return Result.Success(GameSessionMapper.ToRoom(session));
    }

    /// <inheritdoc />
    public async Task<bool> LeaveGameAsync(string roomCode, long userId, bool isExplicitLeave = false)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return false;

        var session = await _store.GetAsync(roomCode);
        if (session is null) return false;

        var player = session.Players.FirstOrDefault(p => p.UserId == userId);
        if (player != null)
        {
            if (isExplicitLeave && player.IsOwner && session.State == GameState.Waiting)
            {
                await CloseRoomAsync(session);
                return true;
            }

            player.IsConnected = false;
            player.DisconnectedAt = DateTime.UtcNow;

            // No transferimos el ownership aquí al vuelo: una desconexión silenciosa
            // (refresh, wifi, pestaña en background en móvil) no distingue de un
            // abandono real. Le damos al owner una ventana de gracia (ver
            // EnsureOwnerActive) para reconectar antes de ceder la sala a otro.

            if (!string.IsNullOrEmpty(player.ConnectionId))
            {
                await _store.ClearConnectionMappingAsync(player.ConnectionId);
            }
        }

        await _store.ClearUserRoomAsync(userId);
        await _store.SaveAsync(session);
        _logger.LogInformation("User {UserId} left room {RoomCode}", userId, roomCode);

        if (player != null && !isExplicitLeave)
        {
            // Desconexión silenciosa (refresh, wifi, cerrar pestaña): el estado
            // IsConnected/IsOwner cambió, pero nadie más se enteraba. Al difundir la
            // lista actualizada, el punto verde/rojo de cada jugador (ya existente en
            // la UI) refleja esto en tiempo real para el resto de la sala.
            await BroadcastPlayersListAsync(session);
        }

        return false;
    }

    /// <summary>
    /// Si el owner lleva desconectado más que la ventana de gracia configurada,
    /// transfiere el ownership al siguiente jugador conectado. Se llama de forma
    /// perezosa (no con un timer) desde los puntos donde importa saber quién es
    /// el owner de verdad: al unirse/reconectar alguien y al intentar iniciar la
    /// partida.
    /// </summary>
    private OwnerCheckResult EnsureOwnerActive(GameSessionDocument session)
    {
        if (session.State != GameState.Waiting) return OwnerCheckResult.Unchanged;

        var owner = session.Players.FirstOrDefault(p => p.IsOwner);
        if (owner is null || owner.IsConnected || owner.DisconnectedAt is null) return OwnerCheckResult.Unchanged;

        var graceExpired = DateTime.UtcNow - owner.DisconnectedAt.Value
            > TimeSpan.FromMinutes(_options.OwnerReconnectGraceMinutes);
        if (!graceExpired) return OwnerCheckResult.Unchanged;

        // Un espectador nunca hereda la sala: si solo quedan espectadores conectados,
        // no hay nadie que pueda jugar ni dirigir la partida y la sala se cierra.
        var newOwner = session.Players.FirstOrDefault(p => p.UserId != owner.UserId && p.IsConnected && !p.IsSpectator);
        if (newOwner is null)
        {
            return session.Players.Any(p => p.UserId != owner.UserId && p.IsConnected)
                ? OwnerCheckResult.MustClose
                : OwnerCheckResult.Unchanged;
        }

        newOwner.IsOwner = true;
        session.OwnerId = newOwner.UserId;
        owner.IsOwner = false;
        _logger.LogInformation(
            "Owner transferred to {NewOwnerId} in room {RoomCode} after {GraceMinutes}min reconnect grace expired",
            newOwner.UserId, session.RoomCode, _options.OwnerReconnectGraceMinutes);
        return OwnerCheckResult.Transferred;
    }

    private enum OwnerCheckResult
    {
        Unchanged,
        Transferred,
        MustClose
    }

    private async Task BroadcastPlayersListAsync(GameSessionDocument session)
    {
        var playersList = session.Players.Select(p => ToPlayerDto(p, session)).ToList();

        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(session.RoomCode).SendAsync("PlayersList", playersList);
    }

    private static PlayerDto ToPlayerDto(PlayerDocument p, GameSessionDocument session) => new(
        p.UserId, p.Username, p.Score, p.CorrectAnswers, p.WrongAnswers, false, p.IsOwner, p.IsConnected, p.IsSpectator,
        ComodinNames(p.AvailableComodines(session.Mode, session.ComodinUsos)),
        UsesByName(p.RemainingUses(session.Mode, session.ComodinUsos)),
        MaxUsesByName(session));

    private static Dictionary<string, int> MaxUsesByName(GameSessionDocument session) =>
        ComodinReglas.Activos(session.Mode, session.ComodinUsos).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

    private static Dictionary<string, int> UsesByName(Dictionary<ComodinTipo, int> uses) =>
        uses.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

    private static List<string> ComodinNames(IEnumerable<ComodinTipo> comodines) =>
        comodines.Select(c => c.ToString()).ToList();

    /// <summary>
    /// Cierra la sala por completo: limpia el mapeo de todos los jugadores, borra la sesión
    /// del store, los saca del grupo de SignalR y difunde <c>RoomClosed</c>.
    /// </summary>
    private async Task CloseRoomAsync(GameSessionDocument session, string reason = "OWNER_LEFT")
    {
        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

        // Avisar antes de sacarlos del grupo: después ya no recibirían el mensaje.
        await hubContext.Clients.Group(session.RoomCode).SendAsync("RoomClosed", new RoomClosedDto(session.RoomCode, reason));

        foreach (var p in session.Players)
        {
            if (!string.IsNullOrEmpty(p.ConnectionId))
            {
                await _store.ClearConnectionMappingAsync(p.ConnectionId);
                await hubContext.Groups.RemoveFromGroupAsync(p.ConnectionId, session.RoomCode);
            }

            await _store.ClearUserRoomAsync(p.UserId);
        }

        await _store.RemoveAsync(session.RoomCode);

        _logger.LogInformation("Room {RoomCode} closed ({Reason})", session.RoomCode, reason);
    }

    /// <inheritdoc />
    public async Task<GameRoom?> StartGameAsync(string roomCode, long userId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return null;

        var session = await _store.GetAsync(roomCode);
        if (session is null) return null;

        switch (EnsureOwnerActive(session))
        {
            case OwnerCheckResult.MustClose:
                await CloseRoomAsync(session, "NO_PLAYERS");
                return null;
            case OwnerCheckResult.Transferred:
                await _store.SaveAsync(session);
                await BroadcastPlayersListAsync(session);
                break;
        }

        if (session.OwnerId != userId)
        {
            _logger.LogWarning("Non-owner {UserId} attempted to start room {RoomCode}", userId, roomCode);
            return null;
        }

        if (session.State is GameState.Playing or GameState.Finished or GameState.Starting)
        {
            _logger.LogInformation("Game in room {RoomCode} already started, finishing, or starting", roomCode);
            return GameSessionMapper.ToRoom(session);
        }

        if (session.State != GameState.Waiting)
        {
            _logger.LogWarning("Start attempt on non-waiting room: {RoomCode}", roomCode);
            return null;
        }

        var activePlayers = session.Players.Where(p => p.IsConnected && !p.IsSpectator).ToList();
        if (activePlayers.Count < _options.MinPlayersToStart)
        {
            _logger.LogWarning("Not enough players to start room {RoomCode}", roomCode);
            return null;
        }

        session.State = GameState.Starting;

        var random = new Random();
        List<Pregunta> questions;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
            questions = await repository.GetQuestionsWithAnswersAsync(session.QuizId) ?? [];

            // Fases con pool: sus preguntas se sortean ahora del banco del autor
            var quiz = await repository.FindByIdAsync(session.QuizId);
            if (quiz is not null && quiz.Pools.Count > 0)
            {
                var banco = scope.ServiceProvider.GetRequiredService<IBancoPreguntaRepository>();
                questions = questions.Concat(await PoolDrawer.DrawAsync(quiz, banco, random)).ToList();
            }
        }

        if (questions.Count == 0)
        {
            _logger.LogWarning("No questions found for quiz {QuizId} in room {RoomCode}", session.QuizId, roomCode);
            session.State = GameState.Waiting;
            await _store.SaveAsync(session);
            return null;
        }

        // Un pool sin preguntas disponibles deja su fase vacía y no se juega: se renumeran las
        // fases que quedan para que la partida muestre "Fase 2 / 2" y no "Fase 3 / 2".
        var renumeracion = questions.Select(q => q.FaseNumero).Distinct().Order()
            .Select((fase, i) => (fase, nueva: i + 1)).ToDictionary(x => x.fase, x => x.nueva);
        foreach (var question in questions)
        {
            question.FaseNumero = renumeracion[question.FaseNumero];
        }

        // Las fases se juegan en orden; el azar solo reordena las preguntas dentro de cada fase.
        questions = questions
            .GroupBy(q => q.FaseNumero)
            .OrderBy(g => g.Key)
            .SelectMany(g => g.OrderBy(_ => random.Next()))
            .ToList();
        foreach (var question in questions)
        {
            question.Respuestas = question.Respuestas.OrderBy(_ => random.Next()).ToList();
        }

        var playerIds = activePlayers.Where(p => !p.IsOwner).Select(p => p.UserId).OrderBy(_ => random.Next()).ToList();
        if (playerIds.Count == 0)
        {
            _logger.LogWarning("No players available for turns in room {RoomCode}", roomCode);
            session.State = GameState.Waiting;
            await _store.SaveAsync(session);
            return null;
        }

        // Todos los jugadores responden las mismas preguntas: se descartan las últimas que sobran.
        questions = FairTurnPlanner.TrimToFair(questions, playerIds.Count, q => Cuestionarios.Entities.TiposPregunta.SinTurno(q.Tipo));

        session.Questions = GameSessionMapper.SnapshotQuestions(questions);
        session.TurnQueue = playerIds;
        session.State = GameState.Playing;
        session.StartedAt = DateTime.UtcNow;
        session.CurrentQuestionIndex = 0;
        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;
        OpenQuestionWithoutTurn(session);

        await StartTurnDeadlineAsync(session);

        _logger.LogInformation("Game started in room {RoomCode} with {QuestionCount} questions", roomCode, questions.Count);

        await BroadcastTurnStartedAsync(session);
        return GameSessionMapper.ToRoom(session);
    }

    /// <inheritdoc />
    public async Task<TurnResultDto?> SubmitAnswerAsync(string roomCode, long userId, long questionId, int answerIndex)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return null;

        var session = await _store.GetAsync(roomCode);
        if (session is null || session.State != GameState.Playing) return null;

        // En modo presencial solo responde el anfitrión (MarkAnswer + ConfirmAnswer).
        if (session.Mode == GameMode.Presencial) return null;

        if (session.GetAnsweringPlayerId() != userId)
        {
            _logger.LogWarning("User {UserId} attempted to answer but it's not their turn in room {RoomCode}", userId, roomCode);
            return null;
        }

        var player = session.Players.FirstOrDefault(p => p.UserId == userId);
        if (player is null || player.IsSpectator) return null;

        if (session.CurrentQuestionIndex >= session.Questions.Count) return null;
        var question = session.Questions[session.CurrentQuestionIndex];
        if (question.Id != questionId) return null;

        // Una respuesta eliminada por la ruleta no se puede elegir.
        if (answerIndex < 0 || answerIndex >= question.Respuestas.Count ||
            session.EliminatedAnswerIndexes.Contains(answerIndex))
        {
            return null;
        }

        // Bonus de tiempo calculado en el servidor: el cliente ya no dicta cuánto le quedaba.
        var remainingSeconds = GetVisibleRemainingSeconds(session);
        await _store.ClearDeadlineAsync(roomCode);

        var isCorrect = IsCorrectAnswer(question, answerIndex);
        return await ResolveAnswerAsync(session, player, question, isCorrect, remainingSeconds, timedOut: false);
    }

    private static bool IsCorrectAnswer(QuestionSnapshot question, int answerIndex)
    {
        var correctAnswer = question.Respuestas.FirstOrDefault(r => r.EsCorrecta);
        var selectedAnswer = question.Respuestas[answerIndex];
        return correctAnswer != null &&
               string.Equals(correctAnswer.Texto, selectedAnswer.Texto, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<Result> StartBuzzerCountdownAsync(string roomCode, long ownerId, long questionId, bool fake)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null) return Result.Failure("Room not found.");
        if (session.OwnerId != ownerId) return Result.Failure("Solo el anfitrión puede empezar la cuenta atrás.");
        if (session.State != GameState.Playing) return Result.Failure("La partida no está en juego.");
        if (session.CurrentQuestionIndex >= session.Questions.Count ||
            session.Questions[session.CurrentQuestionIndex].Id != questionId)
        {
            return Result.Failure("La pregunta ya ha cambiado.");
        }
        if (!session.BuzzerOpen) return Result.Failure("No hay ningún pulsador esperando.");
        if (!BuzzerWaitingForHost(session)) return Result.Failure("La cuenta atrás ya ha empezado.");

        if (!fake)
        {
            // De verdad: el pulsador se abre al terminar la cuenta atrás y desde ahí corre el tiempo para pulsar
            session.BuzzerOpensAtUnixMs = DateTimeOffset.UtcNow.AddMilliseconds(BuzzerCountdownMs).ToUnixTimeMilliseconds();
            await StartTurnDeadlineAsync(session);
        }

        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(roomCode).SendAsync("BuzzerCountdown",
            new BuzzerCountdownDto(questionId, fake, BuzzerCountdownMs, GetTurnLimitSeconds(session)));
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DismissCallAsync(string roomCode, long ownerId, long questionId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        var check = CheckHostCanAnswer(session, ownerId, questionId);
        if (check.IsFailure) return check;

        if (!session!.CallActive) return Result.Success();

        session.CallActive = false;
        await _store.SaveAsync(session);

        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(roomCode).SendAsync("CallDismissed", new CallDismissedDto(questionId));
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> MarkAnswerAsync(string roomCode, long ownerId, long questionId, int? answerIndex)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        var check = CheckHostCanAnswer(session, ownerId, questionId);
        if (check.IsFailure) return check;

        var question = session!.Questions[session.CurrentQuestionIndex];
        if (answerIndex is { } index &&
            (index < 0 || index >= question.Respuestas.Count || session.EliminatedAnswerIndexes.Contains(index)))
        {
            return Result.Failure("Esa respuesta no se puede marcar.");
        }

        session.MarkedAnswerIndex = answerIndex;
        await _store.SaveAsync(session);
        await BroadcastAnswerMarkedAsync(session);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<TurnResultDto>> ConfirmAnswerAsync(string roomCode, long ownerId, long questionId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure<TurnResultDto>("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        var check = CheckHostCanAnswer(session, ownerId, questionId);
        if (check.IsFailure) return Result.Failure<TurnResultDto>(check.Error);

        if (session!.MarkedAnswerIndex is not { } answerIndex)
        {
            return Result.Failure<TurnResultDto>("Marca una respuesta antes de confirmarla.");
        }

        var player = session.Players.FirstOrDefault(p => p.UserId == session.GetAnsweringPlayerId());
        if (player is null) return Result.Failure<TurnResultDto>("No hay ningún jugador respondiendo.");

        var question = session.Questions[session.CurrentQuestionIndex];
        var result = await ResolveAnswerAsync(session, player, question, IsCorrectAnswer(question, answerIndex),
            remainingSeconds: 0, timedOut: false);
        return Result.Success(result);
    }

    /// <inheritdoc />
    public async Task<Result> NextQuestionAsync(string roomCode, long ownerId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null) return Result.Failure("Room not found.");
        if (session.OwnerId != ownerId) return Result.Failure("Solo el anfitrión puede pasar de pregunta.");
        if (session.State != GameState.Playing) return Result.Failure("La partida no está en juego.");
        if (!session.AwaitingNextQuestion && !session.IsWaitingForWinner)
        {
            return Result.Failure("Todavía no se ha confirmado la respuesta.");
        }

        // Con el pulsador o la prueba de colores abiertos, el anfitrión puede saltar la pregunta.
        if (session.IsWaitingForWinner)
        {
            await _store.ClearDeadlineAsync(roomCode);
        }
        await AdvanceToNextTurnAsync(session);
        return Result.Success();
    }

    /// <summary>Validaciones comunes para que el anfitrión marque o confirme en modo presencial.</summary>
    private static Result CheckHostCanAnswer(GameSessionDocument? session, long ownerId, long questionId)
    {
        if (session is null) return Result.Failure("Room not found.");
        if (session.Mode != GameMode.Presencial) return Result.Failure("Solo disponible en modo presencial.");
        if (session.OwnerId != ownerId) return Result.Failure("Solo el anfitrión puede marcar respuestas.");
        if (session.State != GameState.Playing) return Result.Failure("La partida no está en juego.");
        if (session.AwaitingNextQuestion) return Result.Failure("La respuesta ya está confirmada.");
        if (session.BuzzerOpen) return Result.Failure("Todavía no ha pulsado ningún equipo.");
        if (session.ColorOpen) return Result.Failure("La prueba de colores aún no ha terminado.");
        if (session.OcarinaOpen) return Result.Failure("Todavía nadie ha tocado la melodía.");
        if (session.CurrentQuestionIndex >= session.Questions.Count ||
            session.Questions[session.CurrentQuestionIndex].Id != questionId)
        {
            return Result.Failure("La pregunta ya ha cambiado.");
        }
        return Result.Success();
    }

    /// <summary>
    /// Aplica el resultado de la respuesta (o del timeout) de quien estaba respondiendo:
    /// puntos, comodines de la pregunta, apuestas y paso al siguiente turno. Si falla un
    /// ladrón, el turno vuelve al jugador original con la misma pregunta.
    /// </summary>
    private async Task<TurnResultDto> ResolveAnswerAsync(
        GameSessionDocument session, PlayerDocument player, QuestionSnapshot question,
        bool isCorrect, int remainingSeconds, bool timedOut)
    {
        var isSteal = session.StealActive && session.StolenById == player.UserId;
        var doubleOrNothing = session.DoubleOrNothingPlayers.Contains(player.UserId);
        // La respuesta cierra la llamada aunque el anfitrión no haya quitado el cartel.
        session.CallActive = false;

        int pointsEarned;
        if (isCorrect)
        {
            if (doubleOrNothing)
            {
                pointsEarned = _options.BasePoints * 2;
            }
            else
            {
                var bonusSeconds = Math.Clamp(remainingSeconds, 0, GetTurnLimitSeconds(session));
                pointsEarned = Math.Min(
                    _options.BasePoints + (bonusSeconds * _options.TimeBonusMultiplier),
                    _options.BasePoints + _options.MaxTimeBonus);
            }
            player.Score += pointsEarned;
            player.CorrectAnswers++;
        }
        else
        {
            var penalty = doubleOrNothing ? _options.BasePoints : isSteal ? _options.BasePoints / 2 : 0;
            pointsEarned = -ApplyPenalty(player, penalty);
            player.WrongAnswers++;
        }

        var correctAnswer = question.Respuestas.FirstOrDefault(r => r.EsCorrecta);
        var correctAnswerIndex = correctAnswer != null ? question.Respuestas.IndexOf(correctAnswer) : -1;

        if (isSteal && !isCorrect)
        {
            // Robo fallido: el original vuelve a responder la misma pregunta con el tiempo
            // completo. No se revela la respuesta correcta todavía.
            var originalId = session.GetCurrentPlayerId();
            var failedSteal = new TurnResultDto(player.UserId, false, -1, pointsEarned, player.Score,
                IsSteal: true, DoubleOrNothing: doubleOrNothing, ReturnsToPlayerId: originalId);

            session.StealActive = false;
            await BroadcastTurnOutcomeAsync(session.RoomCode, failedSteal, timedOut);
            await RestartCurrentTurnAsync(session);
            return failedSteal;
        }

        var bets = ResolveBets(session, originalAnswered: !isSteal, isCorrect);
        var turnResult = new TurnResultDto(player.UserId, isCorrect, correctAnswerIndex, pointsEarned, player.Score,
            IsSteal: isSteal, DoubleOrNothing: doubleOrNothing, Bets: bets);

        await BroadcastTurnOutcomeAsync(session.RoomCode, turnResult, timedOut);

        if (session.Mode == GameMode.Presencial)
        {
            // El resultado se queda en pantalla hasta que el anfitrión pase de pregunta (NextQuestion).
            session.AwaitingNextQuestion = true;
            session.MarkedAnswerIndex = null;
            session.LastTurnResult = turnResult;
            await _store.SaveAsync(session);
            return turnResult;
        }

        await AdvanceToNextTurnAsync(session);
        return turnResult;
    }

    /// <summary>Resta puntos sin bajar de 0; devuelve lo que realmente se restó.</summary>
    private static int ApplyPenalty(PlayerDocument player, int penalty)
    {
        var applied = Math.Min(penalty, player.Score);
        player.Score -= applied;
        return applied;
    }

    /// <summary>
    /// Liquida las apuestas de la pregunta. Si la respondió el ladrón (y acertó), el jugador
    /// en turno nunca llegó a responder: las apuestas se anulan y se devuelve el comodín.
    /// </summary>
    private List<BetResultDto> ResolveBets(GameSessionDocument session, bool originalAnswered, bool isCorrect)
    {
        var results = new List<BetResultDto>();
        foreach (var bet in session.Bets)
        {
            var bettor = session.Players.FirstOrDefault(p => p.UserId == bet.UserId);
            if (bettor is null) continue;

            if (!originalAnswered)
            {
                bettor.UsedComodines.Remove(ComodinTipo.Apuesta);
                results.Add(new BetResultDto(bettor.UserId, bet.PredictsCorrect, false, true, 0, bettor.Score));
                continue;
            }

            var won = bet.PredictsCorrect == isCorrect;
            var points = won ? _options.BasePoints / 2 : 0;
            bettor.Score += points;
            results.Add(new BetResultDto(bettor.UserId, bet.PredictsCorrect, won, false, points, bettor.Score));
        }
        return results;
    }

    /// <summary>Devuelve la pregunta actual al jugador en turno con el tiempo completo (tras un robo fallido o anulado).</summary>
    private async Task RestartCurrentTurnAsync(GameSessionDocument session)
    {
        session.StealActive = false;
        session.MarkedAnswerIndex = null;
        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;
        await _store.ClearDeadlineAsync(session.RoomCode);
        await StartTurnDeadlineAsync(session);
        await BroadcastTurnStartedAsync(session);
    }

    /// <inheritdoc />
    public async Task<Result> BuzzAsync(string roomCode, long userId, long questionId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null) return Result.Failure("Room not found.");
        if (session.State != GameState.Playing) return Result.Failure("La partida no está en juego.");

        var player = session.Players.FirstOrDefault(p => p.UserId == userId);
        if (player is null || !player.CanPlay()) return Result.Failure("Solo los jugadores pueden pulsar.");

        if (session.CurrentQuestionIndex >= session.Questions.Count ||
            session.Questions[session.CurrentQuestionIndex].Id != questionId)
        {
            return Result.Failure("La pregunta ya ha cambiado.");
        }

        // Dentro del lock el primero que llega gana; el resto se encuentra el pulsador cerrado.
        if (!session.BuzzerOpen) return Result.Failure("Otro equipo ha pulsado antes.");
        if (BuzzerWaitingForHost(session)) return Result.Failure("Espera a que el anfitrión empiece la cuenta atrás.");
        if (BuzzerLockedRemainingMs(session) > 0) return Result.Failure("Aún no se puede pulsar.");

        session.BuzzerOpen = false;
        session.BuzzWinnerId = userId;
        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;
        await _store.ClearDeadlineAsync(roomCode);
        await StartTurnDeadlineAsync(session);

        _logger.LogInformation("User {UserId} buzzed first in room {RoomCode}", userId, roomCode);

        using (var scope = _scopeFactory.CreateScope())
        {
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
            await hubContext.Clients.Group(roomCode).SendAsync("BuzzerWon", new BuzzerWonDto(questionId, userId, player.Username));
        }

        await BroadcastTurnStartedAsync(session);
        return Result.Success();
    }

    /// <summary>
    /// Pregunta por la que cambiar la actual: una aún no jugada de la misma fase (sin pulsador), al azar.
    /// Se intercambian de sitio, así que nada se repite ni se pierde y el total de preguntas no cambia.
    /// </summary>
    private static int? PickReplacementQuestionIndex(GameSessionDocument session)
    {
        var current = session.Questions[session.CurrentQuestionIndex];
        var candidates = Enumerable.Range(session.CurrentQuestionIndex + 1, session.Questions.Count - session.CurrentQuestionIndex - 1)
            .Where(i => session.Questions[i].FaseNumero == current.FaseNumero && !session.Questions[i].SinTurno)
            .ToList();
        return candidates.Count == 0 ? null : candidates[Random.Shared.Next(candidates.Count)];
    }

    /// <summary>Índices de respuestas incorrectas que aún no se han eliminado en la pregunta en curso.</summary>
    private static List<int> IncorrectAnswersLeft(GameSessionDocument session, QuestionSnapshot question) =>
        Enumerable.Range(0, question.Respuestas.Count)
            .Where(i => !question.Respuestas[i].EsCorrecta && !session.EliminatedAnswerIndexes.Contains(i))
            .ToList();

    /// <summary>
    /// Elimina al azar hasta <paramref name="count"/> respuestas incorrectas. Si el anfitrión tenía marcada una de
    /// ellas, la desmarca (<c>Unmarked</c>).
    /// </summary>
    private static (List<int> Eliminated, bool Unmarked) EliminateIncorrectAnswers(
        GameSessionDocument session, QuestionSnapshot question, int count)
    {
        var eliminated = IncorrectAnswersLeft(session, question)
            .OrderBy(_ => Random.Shared.Next()).Take(count).OrderBy(i => i).ToList();
        session.EliminatedAnswerIndexes.AddRange(eliminated);
        var unmarked = false;
        if (session.MarkedAnswerIndex is { } marked && eliminated.Contains(marked))
        {
            session.MarkedAnswerIndex = null;
            unmarked = true;
        }
        return (eliminated, unmarked);
    }

    /// <inheritdoc />
    public async Task<Result<ComodinUsedDto>> UseComodinAsync(
        string roomCode, long userId, ComodinTipo tipo, long questionId, bool? predictsCorrect = null)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure<ComodinUsedDto>("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null) return Result.Failure<ComodinUsedDto>("Room not found.");

        if (session.State != GameState.Playing)
        {
            return Result.Failure<ComodinUsedDto>("Los comodines solo se pueden usar con la partida en juego.");
        }

        if (session.AwaitingNextQuestion)
        {
            return Result.Failure<ComodinUsedDto>("La respuesta ya está confirmada.");
        }

        var player = session.Players.FirstOrDefault(p => p.UserId == userId);
        if (player is null || !player.CanPlay())
        {
            return Result.Failure<ComodinUsedDto>("Solo los jugadores pueden usar comodines.");
        }

        // El questionId evita que un clic tardío se aplique a la pregunta siguiente.
        if (session.CurrentQuestionIndex >= session.Questions.Count ||
            session.Questions[session.CurrentQuestionIndex].Id != questionId)
        {
            return Result.Failure<ComodinUsedDto>("La pregunta ya ha cambiado.");
        }

        if (session.IsCurrentQuestionDynamic())
        {
            return Result.Failure<ComodinUsedDto>("En las rondas dinámicas no hay comodines.");
        }

        var activos = ComodinReglas.Activos(session.Mode, session.ComodinUsos);
        if (!activos.TryGetValue(tipo, out var usosMaximos))
        {
            return Result.Failure<ComodinUsedDto>("Ese comodín no está activo en esta partida.");
        }

        if (player.UsedComodines.Count(c => c == tipo) >= usosMaximos)
        {
            return Result.Failure<ComodinUsedDto>(usosMaximos == 1 ? "Ya has usado ese comodín." : "Ya has gastado los usos de ese comodín.");
        }

        var question = session.Questions[session.CurrentQuestionIndex];
        var turnOwnerId = session.GetCurrentPlayerId();
        var answeringId = session.GetAnsweringPlayerId();

        if (ComodinReglas.EsDeTurno(tipo) && answeringId != userId)
        {
            return Result.Failure<ComodinUsedDto>("Ese comodín solo se puede usar en tu turno.");
        }

        if (!ComodinReglas.EsDeTurno(tipo) && (turnOwnerId == userId || answeringId == userId))
        {
            return Result.Failure<ComodinUsedDto>("Ese comodín solo se puede usar fuera de tu turno.");
        }

        List<int>? eliminated = null;
        int? ruletaResultado = null;
        int? ruletaHueco = null;
        long? stolenFrom = null;
        long? targetPlayer = null;
        var questionChanged = false;
        var unmarked = false;

        switch (tipo)
        {
            case ComodinTipo.Ruleta:
            {
                if (IncorrectAnswersLeft(session, question).Count == 0)
                {
                    return Result.Failure<ComodinUsedDto>("No quedan respuestas incorrectas que eliminar.");
                }
                var (hueco, valor) = ComodinReglas.TirarRuleta(Random.Shared);
                ruletaHueco = hueco;
                (eliminated, unmarked) = EliminateIncorrectAnswers(session, question, valor);
                ruletaResultado = eliminated.Count;

                // El giro no descuenta tiempo: el plazo del turno se alarga lo que dura la animación.
                if (session.TurnDeadlineUnixMs.HasValue)
                {
                    session.TurnDeadlineUnixMs += ComodinReglas.DuracionRuletaMs;
                    await _store.ScheduleDeadlineAsync(roomCode, session.TurnDeadlineUnixMs.Value, session.TurnGeneration);
                }
                break;
            }
            case ComodinTipo.CincuentaCincuenta:
            {
                // Solo cuenta lo que aún queda (la ruleta u otro 50/50 pueden haber eliminado ya algunas)
                var aEliminar = ComodinReglas.EliminadasCincuentaCincuenta(IncorrectAnswersLeft(session, question).Count);
                if (aEliminar == 0)
                {
                    return Result.Failure<ComodinUsedDto>("No quedan respuestas incorrectas que eliminar.");
                }
                (eliminated, unmarked) = EliminateIncorrectAnswers(session, question, aEliminar);
                break;
            }
            case ComodinTipo.CambiarPregunta:
            case ComodinTipo.CambiarPreguntaRival:
            {
                if (session.StealActive)
                {
                    return Result.Failure<ComodinUsedDto>("No se puede cambiar una pregunta robada.");
                }
                var replacementIndex = PickReplacementQuestionIndex(session);
                if (replacementIndex is null)
                {
                    return Result.Failure<ComodinUsedDto>("No quedan preguntas de esta fase para cambiar.");
                }
                (session.Questions[session.CurrentQuestionIndex], session.Questions[replacementIndex.Value]) =
                    (session.Questions[replacementIndex.Value], session.Questions[session.CurrentQuestionIndex]);
                // Lo eliminado o marcado era de la pregunta anterior.
                session.EliminatedAnswerIndexes.Clear();
                session.MarkedAnswerIndex = null;
                if (tipo == ComodinTipo.CambiarPreguntaRival) targetPlayer = answeringId;
                questionChanged = true;
                break;
            }
            case ComodinTipo.OcultarTexto:
                if (session.StealActive)
                {
                    return Result.Failure<ComodinUsedDto>("No se puede ocultar el texto mientras la pregunta está robada.");
                }
                if (session.TextHiddenForPlayerId.HasValue)
                {
                    return Result.Failure<ComodinUsedDto>("El texto de esta pregunta ya está oculto.");
                }
                if (answeringId is null)
                {
                    return Result.Failure<ComodinUsedDto>("Nadie está respondiendo ahora mismo.");
                }
                targetPlayer = answeringId;
                session.TextHiddenForPlayerId = answeringId;
                break;
            case ComodinTipo.DobleONada:
                session.DoubleOrNothingPlayers.Add(userId);
                break;
            case ComodinTipo.Robo:
                if (session.StolenById.HasValue)
                {
                    return Result.Failure<ComodinUsedDto>("Esta pregunta ya ha sido robada.");
                }
                if (session.ComodinUsedOnQuestion)
                {
                    return Result.Failure<ComodinUsedDto>("No se puede robar una pregunta en la que ya se ha usado un comodín.");
                }
                stolenFrom = turnOwnerId;
                session.StolenById = userId;
                session.StealActive = true;
                // Ahora responde el ladrón: lo que hubiera marcado el anfitrión ya no vale.
                session.MarkedAnswerIndex = null;
                break;
            case ComodinTipo.Apuesta:
                if (predictsCorrect is null)
                {
                    return Result.Failure<ComodinUsedDto>("Indica si apuestas a que acierta o a que falla.");
                }
                if (session.StealActive)
                {
                    return Result.Failure<ComodinUsedDto>("No se puede apostar mientras la pregunta está robada.");
                }
                if (session.StolenById == userId)
                {
                    return Result.Failure<ComodinUsedDto>("No puedes apostar sobre una pregunta que has robado.");
                }
                session.Bets.Add(new BetDocument { UserId = userId, PredictsCorrect = predictsCorrect.Value });
                break;
            case ComodinTipo.Llamada:
                if (session.CallActive)
                {
                    return Result.Failure<ComodinUsedDto>("Ya hay una llamada en curso.");
                }
                session.CallActive = true;
                break;
        }

        // El robo en sí no cierra nada: lo que bloquea el robo es que otro comodín se haya usado antes.
        if (tipo != ComodinTipo.Robo)
        {
            session.ComodinUsedOnQuestion = true;
        }
        player.UsedComodines.Add(tipo);

        var dto = new ComodinUsedDto(
            userId, player.Username, tipo.ToString(), question.Id, ComodinNames(player.AvailableComodines(session.Mode, session.ComodinUsos)),
            eliminated, ruletaResultado, tipo == ComodinTipo.Apuesta ? predictsCorrect : null, stolenFrom,
            ruletaHueco, ruletaHueco.HasValue ? ComodinReglas.DuracionRuletaMs : null,
            UsesByName(player.RemainingUses(session.Mode, session.ComodinUsos)), targetPlayer);

        using (var scope = _scopeFactory.CreateScope())
        {
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
            await hubContext.Clients.Group(roomCode).SendAsync("ComodinUsed", dto);
        }

        if (unmarked && !questionChanged)
        {
            await BroadcastAnswerMarkedAsync(session);
        }

        if (questionChanged)
        {
            // Pregunta nueva para quien responde: tiempo completo, igual que al empezar el turno.
            session.TurnStartedAt = DateTime.UtcNow;
            session.TurnGeneration++;
            await _store.ClearDeadlineAsync(roomCode);
            await StartTurnDeadlineAsync(session);
            await BroadcastTurnStartedAsync(session);
        }
        else if (tipo == ComodinTipo.Robo)
        {
            // El ladrón responde con el tiempo completo; el deadline del original queda obsoleto por la nueva generación.
            session.TurnStartedAt = DateTime.UtcNow;
            session.TurnGeneration++;
            await _store.ClearDeadlineAsync(roomCode);
            await StartTurnDeadlineAsync(session);
            await BroadcastTurnStartedAsync(session);
        }
        else
        {
            await _store.SaveAsync(session);
        }

        _logger.LogInformation("User {UserId} used comodín {Tipo} in room {RoomCode}", userId, tipo, roomCode);
        return Result.Success(dto);
    }

    /// <inheritdoc />
    public async Task ProcessDueTimeoutAsync(string roomCode, long expectedTurnGeneration)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return;

        var session = await _store.GetAsync(roomCode);
        if (session is null) return;

        if (session.State != GameState.Playing)
        {
            await _store.ClearDeadlineAsync(roomCode);
            return;
        }

        if (session.TurnGeneration != expectedTurnGeneration)
        {
            _logger.LogInformation(
                "[TIMEOUT] Ignoring stale deadline for room {RoomCode}. Expected gen {Expected}, actual {Actual}",
                roomCode, expectedTurnGeneration, session.TurnGeneration);
            return;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (session.TurnDeadlineUnixMs.HasValue && session.TurnDeadlineUnixMs.Value > nowMs)
        {
            return;
        }

        if (session.BuzzerOpen)
        {
            // Nadie pulsó a tiempo: la pregunta se pasa sin puntos para nadie.
            await _store.ClearDeadlineAsync(roomCode);
            await AdvanceToNextTurnAsync(session);
            return;
        }

        if (session.ColorOpen)
        {
            // Se acabó el tiempo de la prueba de colores: compiten los colores enviados hasta ahora.
            await _store.ClearDeadlineAsync(roomCode);
            await ResolveColorChallengeAsync(session);
            return;
        }

        var answeringPlayerId = session.GetAnsweringPlayerId();
        if (answeringPlayerId is null)
        {
            await _store.ClearDeadlineAsync(roomCode);
            return;
        }

        var player = session.Players.FirstOrDefault(p => p.UserId == answeringPlayerId);
        if (player is null)
        {
            await _store.ClearDeadlineAsync(roomCode);
            return;
        }

        if (session.CurrentQuestionIndex >= session.Questions.Count)
        {
            player.WrongAnswers++;
            await EndGameAsync(session);
            return;
        }

        await _store.ClearDeadlineAsync(roomCode);
        var question = session.Questions[session.CurrentQuestionIndex];
        await ResolveAnswerAsync(session, player, question, isCorrect: false, remainingSeconds: 0, timedOut: true);
    }

    /// <inheritdoc />
    public async Task<Result> PauseGameAsync(string roomCode, long userId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null)
        {
            return Result.Failure("Room not found.");
        }

        if (session.OwnerId != userId)
        {
            return Result.Failure("Only the owner can pause the game.");
        }

        if (session.State != GameState.Playing)
        {
            return Result.Failure("Game can only be paused when playing.");
        }

        if (session.TurnDeadlineUnixMs.HasValue)
        {
            var remainingMs = session.TurnDeadlineUnixMs.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            session.PausedTimeRemaining = Math.Max(0, (int)Math.Ceiling(remainingMs / 1000.0));
        }
        else if (session.TurnStartedAt.HasValue && GetTurnLimitSeconds(session) > 0 && !BuzzerWaitingForHost(session))
        {
            var elapsed = (DateTime.UtcNow - session.TurnStartedAt.Value).TotalSeconds;
            session.PausedTimeRemaining = Math.Max(0, GetTurnLimitSeconds(session) + 1 - (int)elapsed);
        }

        // La cuenta atrás del pulsador se congela con la pausa
        session.PausedBuzzerLockMs = BuzzerLockedRemainingMs(session) is > 0 and var lockMs ? lockMs : null;

        session.State = GameState.Paused;
        session.TurnDeadlineUnixMs = null;
        session.TurnGeneration++;

        await _store.ClearDeadlineAsync(roomCode);
        await _store.SaveAsync(session);
        await BroadcastGamePausedAsync(roomCode);

        _logger.LogInformation("Game paused in room {RoomCode} by owner {UserId}. Time remaining: {TimeRemaining}s",
            roomCode, userId, session.PausedTimeRemaining);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ResumeGameAsync(string roomCode, long userId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null)
        {
            return Result.Failure("Room not found.");
        }

        if (session.OwnerId != userId)
        {
            return Result.Failure("Only the owner can resume the game.");
        }

        if (session.State != GameState.Paused)
        {
            return Result.Failure("Game can only be resumed when paused.");
        }

        var hasLimit = GetTurnLimitSeconds(session) > 0 && !BuzzerWaitingForHost(session);
        var timeToResume = hasLimit ? (session.PausedTimeRemaining ?? GetTurnLimitSeconds(session) + 1) : 0;
        session.PausedTimeRemaining = null;
        if (session.PausedBuzzerLockMs is { } lockMs)
        {
            session.BuzzerOpensAtUnixMs = DateTimeOffset.UtcNow.AddMilliseconds(lockMs).ToUnixTimeMilliseconds();
            session.PausedBuzzerLockMs = null;
        }
        session.State = GameState.Playing;
        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;

        if (hasLimit)
        {
            session.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(timeToResume).ToUnixTimeMilliseconds();
            await _store.SaveAsync(session);
            await _store.ScheduleDeadlineAsync(roomCode, session.TurnDeadlineUnixMs.Value, session.TurnGeneration);
        }
        else
        {
            session.TurnDeadlineUnixMs = null;
            await _store.SaveAsync(session);
        }
        await BroadcastGameResumedAsync(roomCode, timeToResume);

        _logger.LogInformation("Game resumed in room {RoomCode} by owner {UserId}. Resuming with {TimeRemaining}s",
            roomCode, userId, timeToResume);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<string?>> KickPlayerAsync(string roomCode, long ownerId, long playerIdToKick)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure<string?>("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null)
        {
            return Result.Failure<string?>("Room not found.");
        }

        if (session.OwnerId != ownerId)
        {
            return Result.Failure<string?>("Only the owner can kick players.");
        }

        if (ownerId == playerIdToKick)
        {
            return Result.Failure<string?>("You cannot kick yourself.");
        }

        var playerToKick = session.Players.FirstOrDefault(p => p.UserId == playerIdToKick);
        if (playerToKick is null)
        {
            return Result.Failure<string?>("Player not found in this room.");
        }

        if (playerToKick.IsOwner)
        {
            return Result.Failure<string?>("The owner cannot be kicked.");
        }

        var connectionIdToRemove = playerToKick.ConnectionId;
        var wasActiveThief = session.StealActive && session.StolenById == playerIdToKick;
        var wasBuzzWinner = session.BuzzWinnerId == playerIdToKick;
        session.Players.Remove(playerToKick);
        session.TurnQueue.RemoveAll(id => id == playerIdToKick);
        session.Bets.RemoveAll(b => b.UserId == playerIdToKick);
        session.ColorGuesses.RemoveAll(g => g.UserId == playerIdToKick);

        await _store.ClearUserRoomAsync(playerIdToKick);
        if (!string.IsNullOrEmpty(connectionIdToRemove))
        {
            await _store.ClearConnectionMappingAsync(connectionIdToRemove);
        }

        var isColorQuestion = session.CurrentQuestionIndex < session.Questions.Count &&
                              session.Questions[session.CurrentQuestionIndex].EsColores;

        if (wasBuzzWinner && isColorQuestion && session.State == GameState.Playing)
        {
            // Expulsado el ganador de la prueba de colores: se la lleva el siguiente que más se acercó.
            session.BuzzWinnerId = null;
            session.ColorOpen = true;
            await _store.ClearDeadlineAsync(roomCode);
            await ResolveColorChallengeAsync(session);
        }
        else if (session.ColorOpen && session.State == GameState.Playing && AllColorGuessesIn(session))
        {
            // El expulsado era el único que faltaba por enviar su color.
            await _store.ClearDeadlineAsync(roomCode);
            await ResolveColorChallengeAsync(session);
        }
        // Expulsado en mitad de un robo: la pregunta vuelve al jugador original con el tiempo completo.
        else if (wasBuzzWinner && session.State == GameState.Playing)
        {
            // Expulsado quien había pulsado o tocado la melodía: la pregunta vuelve a abrirse a todos
            // (en la ocarina la melodía ya sonó, así que se puede tocar enseguida).
            session.BuzzWinnerId = null;
            if (session.Questions[session.CurrentQuestionIndex].EsOcarina)
            {
                session.OcarinaOpen = true;
            }
            else
            {
                session.BuzzerOpen = true;
            }
            await RestartCurrentTurnAsync(session);
        }
        else if (wasActiveThief && session.State == GameState.Playing)
        {
            await RestartCurrentTurnAsync(session);
        }
        else
        {
            if (wasActiveThief)
            {
                session.StealActive = false;
                session.PausedTimeRemaining = null;
            }
            await _store.SaveAsync(session);
        }

        _logger.LogInformation("Player {PlayerIdToKick} kicked from room {RoomCode} by owner {OwnerId}",
            playerIdToKick, roomCode, ownerId);

        return Result.Success<string?>(connectionIdToRemove);
    }

    /// <inheritdoc />
    public async Task<Result> ReviveComodinAsync(string roomCode, long ownerId, long targetUserId, ComodinTipo? tipo)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null) return Result.Failure("Room not found.");
        if (session.OwnerId != ownerId) return Result.Failure("Only the owner can revive comodines.");
        if (session.State is GameState.Waiting or GameState.Starting or GameState.Finished) return Result.Failure("Comodines can only be revived during the game.");

        var target = session.Players.FirstOrDefault(p => p.UserId == targetUserId);
        if (target is null) return Result.Failure("Player not found in this room.");
        if (!target.CanPlay()) return Result.Failure("Ese jugador no usa comodines.");

        var revived = tipo is { } t ? (target.UsedComodines.Remove(t) ? 1 : 0) : target.UsedComodines.RemoveAll(_ => true);
        if (revived == 0) return Result.Failure("El jugador no tiene comodines usados que revivir.");

        await _store.SaveAsync(session);
        await BroadcastPlayersListAsync(session);

        _logger.LogInformation("Owner {OwnerId} revived {Tipo} for player {TargetUserId} in room {RoomCode}",
            ownerId, tipo?.ToString() ?? "all comodines", targetUserId, roomCode);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetSpectatorAsync(string roomCode, long ownerId, long targetUserId, bool isSpectator)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null)
        {
            return Result.Failure("Room not found.");
        }

        if (session.OwnerId != ownerId)
        {
            return Result.Failure("Only the owner can assign spectators.");
        }

        if (session.State != GameState.Waiting)
        {
            return Result.Failure("Spectators can only be assigned in the lobby.");
        }

        var target = session.Players.FirstOrDefault(p => p.UserId == targetUserId);
        if (target is null)
        {
            return Result.Failure("Player not found in this room.");
        }

        if (target.IsOwner)
        {
            return Result.Failure("The owner cannot be a spectator.");
        }

        target.IsSpectator = isSpectator;
        await _store.SaveAsync(session);
        await BroadcastPlayersListAsync(session);

        _logger.LogInformation("Player {TargetUserId} in room {RoomCode} set as {Role} by owner {OwnerId}",
            targetUserId, roomCode, isSpectator ? "spectator" : "player", ownerId);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task HandleDisconnectionAsync(string connectionId)
    {
        _logger.LogInformation("Handling disconnection for connection {ConnectionId}", connectionId);

        var mapping = await _store.GetConnectionMappingAsync(connectionId);
        if (mapping is null)
        {
            _logger.LogWarning("Connection {ConnectionId} not found in connection mapping", connectionId);
            return;
        }

        await _store.ClearConnectionMappingAsync(connectionId);
        await LeaveGameAsync(mapping.RoomCode, mapping.UserId);
    }

    /// <inheritdoc />
    public async Task<string?> GetRoomCodeByConnectionAsync(string connectionId)
    {
        var mapping = await _store.GetConnectionMappingAsync(connectionId);
        return mapping?.RoomCode;
    }

    private async Task AdvanceToNextTurnAsync(GameSessionDocument session)
    {
        session.ResetQuestionState();
        session.CurrentQuestionIndex++;
        if (session.CurrentQuestionIndex >= session.Questions.Count)
        {
            await EndGameAsync(session);
            return;
        }

        // Al cambiar de fase se hace un intermedio: sin turno ni deadline hasta que el owner continúe.
        var previous = session.Questions[session.CurrentQuestionIndex - 1];
        var next = session.Questions[session.CurrentQuestionIndex];
        if (next.FaseNumero != previous.FaseNumero)
        {
            session.State = GameState.PhaseBreak;
            session.TurnDeadlineUnixMs = null;
            session.TurnGeneration++;

            await _store.ClearDeadlineAsync(session.RoomCode);
            await _store.SaveAsync(session);
            await BroadcastPhaseCompletedAsync(session);
            return;
        }

        await StartNextTurnAsync(session);
    }

    private async Task StartNextTurnAsync(GameSessionDocument session)
    {
        // Una pregunta sin turno (pulsador o colores) no gasta turno: la cola solo avanza tras una pregunta por turno.
        var previousWasBuzzer = session.CurrentQuestionIndex > 0 && session.Questions[session.CurrentQuestionIndex - 1].SinTurno;
        var nextPlayerId = previousWasBuzzer ? session.TurnQueue.Cast<long?>().FirstOrDefault() : session.RotateTurn();
        if (nextPlayerId is null)
        {
            await EndGameAsync(session);
            return;
        }

        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;
        OpenQuestionWithoutTurn(session);

        await StartTurnDeadlineAsync(session);
        await BroadcastTurnStartedAsync(session);
    }

    /// <summary>
    /// Al empezar una pregunta sin turno abre su fase de espera: el pulsador, la prueba de colores con un
    /// color objetivo al azar, o la ocarina con una melodía al azar (distintos en cada pregunta).
    /// </summary>
    private static void OpenQuestionWithoutTurn(GameSessionDocument session)
    {
        var question = session.CurrentQuestionIndex < session.Questions.Count ? session.Questions[session.CurrentQuestionIndex] : null;
        // El pulsador queda cerrado hasta que el anfitrión lance la cuenta atrás (StartBuzzerCountdownAsync)
        session.BuzzerOpen = question?.EsPulsador == true;
        session.BuzzerOpensAtUnixMs = null;
        session.ColorOpen = question?.EsColores == true;
        session.ColorTarget = session.ColorOpen ? ColorMatch.RandomTarget(Random.Shared) : null;
        session.ColorGuesses = new();
        session.OcarinaOpen = question?.EsOcarina == true;
        session.OcarinaMelody = session.OcarinaOpen ? OcarinaMelody.Random(Random.Shared) : null;
        session.OcarinaListenUntilUnixMs = session.OcarinaOpen
            ? DateTimeOffset.UtcNow.AddMilliseconds(OcarinaMelody.PlaybackMs(session.OcarinaMelody!)).ToUnixTimeMilliseconds()
            : null;
    }

    /// <summary>Cuenta atrás (5-4-3-2-1) que lanza el anfitrión antes de abrir el pulsador.</summary>
    public const int BuzzerCountdownMs = 5000;

    /// <summary>Pulsador abierto pero esperando a que el anfitrión lance la cuenta atrás de verdad.</summary>
    private static bool BuzzerWaitingForHost(GameSessionDocument session) =>
        session.BuzzerOpen && session.BuzzerOpensAtUnixMs is null;

    /// <summary>Lo que falta para poder pulsar (0 si ya se puede o no hay pulsador).</summary>
    private static int BuzzerLockedRemainingMs(GameSessionDocument session) =>
        session.BuzzerOpen && session.BuzzerOpensAtUnixMs is { } opensAt
            ? (int)Math.Max(0, opensAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            : 0;

    /// <summary>Lo que le queda a la melodía de la ocarina por sonar (0 si ya ha terminado o no hay).</summary>
    private static int OcarinaListenRemainingMs(GameSessionDocument session) =>
        session.OcarinaListenUntilUnixMs is { } until
            ? (int)Math.Max(0, until - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            : 0;

    /// <inheritdoc />
    public async Task<Result<bool>> SubmitOcarinaAsync(string roomCode, long userId, long questionId, IReadOnlyList<int> notes)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure<bool>("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null) return Result.Failure<bool>("Room not found.");
        if (session.State != GameState.Playing) return Result.Failure<bool>("La partida no está en juego.");

        var player = session.Players.FirstOrDefault(p => p.UserId == userId);
        if (player is null || !player.CanPlay()) return Result.Failure<bool>("Solo los jugadores pueden tocar la ocarina.");

        if (session.CurrentQuestionIndex >= session.Questions.Count ||
            session.Questions[session.CurrentQuestionIndex].Id != questionId)
        {
            return Result.Failure<bool>("La pregunta ya ha cambiado.");
        }

        if (!session.OcarinaOpen || session.OcarinaMelody is null) return Result.Failure<bool>("Otro equipo ya ha tocado la melodía.");
        if (OcarinaListenRemainingMs(session) > 0) return Result.Failure<bool>("Espera a que termine de sonar la melodía.");
        if (notes.Any(n => n is < 0 or >= OcarinaMelody.PitchCount)) return Result.Failure<bool>("Nota no válida.");

        if (!OcarinaMelody.Matches(session.OcarinaMelody, notes))
        {
            // Fallo: solo lo sabe quien lo intenta, que puede volver a probar.
            return Result.Success(false);
        }

        // Dentro del lock el primero que acierta gana; los demás se encuentran la prueba cerrada.
        session.OcarinaOpen = false;
        session.BuzzWinnerId = userId;
        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;
        await _store.ClearDeadlineAsync(roomCode);
        await StartTurnDeadlineAsync(session);

        _logger.LogInformation("User {UserId} played the ocarina melody first in room {RoomCode}", userId, roomCode);

        using (var scope = _scopeFactory.CreateScope())
        {
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
            await hubContext.Clients.Group(roomCode).SendAsync("OcarinaWon", new OcarinaWonDto(questionId, userId, player.Username));
        }

        await BroadcastTurnStartedAsync(session);
        return Result.Success(true);
    }

    /// <summary>Jugadores que compiten en la prueba de colores: los que juegan y siguen conectados.</summary>
    private static IEnumerable<PlayerDocument> ColorContenders(GameSessionDocument session) =>
        session.Players.Where(p => p.CanPlay() && p.IsConnected);

    private static bool AllColorGuessesIn(GameSessionDocument session)
    {
        var contenders = ColorContenders(session).Select(p => p.UserId).ToList();
        return contenders.Count > 0 && contenders.All(id => session.ColorGuesses.Any(g => g.UserId == id));
    }

    /// <inheritdoc />
    public async Task<Result> SubmitColorAsync(string roomCode, long userId, long questionId, ColorHsb color)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null) return Result.Failure("Room not found.");
        if (session.State != GameState.Playing) return Result.Failure("La partida no está en juego.");

        var player = session.Players.FirstOrDefault(p => p.UserId == userId);
        if (player is null || !player.CanPlay()) return Result.Failure("Solo los jugadores pueden participar.");

        if (session.CurrentQuestionIndex >= session.Questions.Count ||
            session.Questions[session.CurrentQuestionIndex].Id != questionId)
        {
            return Result.Failure("La pregunta ya ha cambiado.");
        }

        if (!session.ColorOpen) return Result.Failure("La prueba de colores ya ha terminado.");
        if (!color.IsValid()) return Result.Failure("Color no válido.");
        if (session.ColorGuesses.Any(g => g.UserId == userId)) return Result.Failure("Ya has enviado tu color.");

        session.ColorGuesses.Add(new ColorGuessDocument { UserId = userId, Color = color });
        _logger.LogInformation("User {UserId} submitted a color in room {RoomCode}", userId, roomCode);

        if (AllColorGuessesIn(session))
        {
            await _store.ClearDeadlineAsync(roomCode);
            await ResolveColorChallengeAsync(session);
            return Result.Success();
        }

        await _store.SaveAsync(session);
        using (var scope = _scopeFactory.CreateScope())
        {
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
            await hubContext.Clients.Group(roomCode).SendAsync("ColorSubmitted", new ColorSubmittedDto(questionId, userId));
        }
        return Result.Success();
    }

    /// <summary>
    /// Cierra la prueba de colores: gana el color más parecido al objetivo (empates al azar) y pasa a responder
    /// la pregunta como quien gana el pulsador. Sin colores enviados, la pregunta se pasa sin puntos.
    /// </summary>
    private async Task ResolveColorChallengeAsync(GameSessionDocument session)
    {
        var question = session.Questions[session.CurrentQuestionIndex];
        var target = session.ColorTarget ?? ColorMatch.RandomTarget(Random.Shared);

        var guesses = session.ColorGuesses
            .Select(g => (Guess: g, Player: session.Players.FirstOrDefault(p => p.UserId == g.UserId)))
            .Where(x => x.Player is not null && x.Player.CanPlay())
            .Select(x => new ColorGuessDto(x.Guess.UserId, x.Player!.Username, x.Guess.Color, ColorMatch.Similarity(target, x.Guess.Color)))
            .OrderByDescending(g => g.Similarity)
            .ToList();

        var best = guesses.Count > 0 ? guesses[0].Similarity : (double?)null;
        var tied = guesses.Where(g => g.Similarity == best).ToList();
        var winner = tied.Count > 0 ? tied[Random.Shared.Next(tied.Count)] : null;
        if (winner is not null && tied.Count > 1)
        {
            // El ganador del sorteo encabeza la lista para que el cliente lo muestre primero.
            guesses.Remove(winner);
            guesses.Insert(0, winner);
        }

        session.ColorOpen = false;

        using (var scope = _scopeFactory.CreateScope())
        {
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
            await hubContext.Clients.Group(session.RoomCode).SendAsync("ColorChallengeResult",
                new ColorChallengeResultDto(question.Id, target, guesses, winner?.PlayerId, winner?.Username, tied.Count > 1));
        }

        if (winner is null)
        {
            _logger.LogInformation("Nobody submitted a color in room {RoomCode}: skipping question", session.RoomCode);
            await AdvanceToNextTurnAsync(session);
            return;
        }

        _logger.LogInformation("User {UserId} won the color challenge in room {RoomCode} ({Similarity}%)",
            winner.PlayerId, session.RoomCode, winner.Similarity);

        session.BuzzWinnerId = winner.PlayerId;
        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;
        await StartTurnDeadlineAsync(session);
        await BroadcastTurnStartedAsync(session);
    }

    /// <inheritdoc />
    public async Task<Result> ContinuePhaseAsync(string roomCode, long userId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return Result.Failure("Room is busy. Please retry.");

        var session = await _store.GetAsync(roomCode);
        if (session is null)
        {
            return Result.Failure("Room not found.");
        }

        if (session.OwnerId != userId)
        {
            return Result.Failure("Only the owner can continue to the next phase.");
        }

        if (session.State != GameState.PhaseBreak)
        {
            return Result.Failure("The game is not between phases.");
        }

        session.State = GameState.Playing;
        await StartNextTurnAsync(session);

        _logger.LogInformation("Room {RoomCode} continued to phase {Phase} by owner {UserId}",
            roomCode, session.Questions[Math.Min(session.CurrentQuestionIndex, session.Questions.Count - 1)].FaseNumero, userId);

        return Result.Success();
    }

    private static int GetTotalPhases(GameSessionDocument session) =>
        session.Questions.Count == 0 ? 1 : session.Questions.Select(q => q.FaseNumero).Distinct().Count();

    private static PhaseCompletedDto BuildPhaseCompleted(GameSessionDocument session)
    {
        var completed = session.Questions[Math.Max(session.CurrentQuestionIndex - 1, 0)];
        var next = session.Questions[Math.Min(session.CurrentQuestionIndex, session.Questions.Count - 1)];
        var players = session.Players.Select(p => ToPlayerDto(p, session)).ToList();

        return new PhaseCompletedDto(
            session.RoomCode,
            completed.FaseNumero,
            completed.FaseNombre,
            next.FaseNombre,
            GetTotalPhases(session),
            players,
            completed.FaseColor,
            next.FaseNumero,
            next.FaseColor);
    }

    private async Task BroadcastPhaseCompletedAsync(GameSessionDocument session)
    {
        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(session.RoomCode).SendAsync("PhaseCompleted", BuildPhaseCompleted(session));
    }

    private async Task EndGameAsync(GameSessionDocument session)
    {
        session.State = GameState.Finished;
        session.EndedAt = DateTime.UtcNow;
        session.TurnDeadlineUnixMs = null;
        session.TurnGeneration++;

        await _store.ClearDeadlineAsync(session.RoomCode);
        await _store.MarkFinishedAsync(session, TimeSpan.FromHours(_options.FinishedRoomTtlHours));

        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

        var filteredPlayers = session.Players
            .Where(p => p.UserId != session.OwnerId && !p.IsSpectator)
            .OrderByDescending(p => p.Score)
            .Select((p, index) => new PlayerResultDto(
                p.UserId,
                p.Username,
                index + 1,
                p.Score,
                p.CorrectAnswers,
                p.WrongAnswers,
                p.CorrectAnswers + p.WrongAnswers > 0
                    ? (int)Math.Round((double)p.CorrectAnswers / (p.CorrectAnswers + p.WrongAnswers) * 100)
                    : 0
            ))
            .ToList();

        var gameResult = new GameResultDto(
            session.RoomCode,
            session.QuizTitle,
            filteredPlayers,
            session.Questions.Count,
            session.EndedAt!.Value - session.StartedAt!.Value
        );

        await hubContext.Clients.Group(session.RoomCode).SendAsync("GameFinished", gameResult);

        try
        {
            var gameHistoryRepo = scope.ServiceProvider.GetRequiredService<IGameHistoryRepository>();
            var history = new GameHistory
            {
                GameId = session.QuizId * 1000 + session.Players.Count,
                QuizId = session.QuizId,
                OwnerId = session.OwnerId,
                QuizTitle = session.QuizTitle,
                StartedAt = session.StartedAt!.Value,
                EndedAt = session.EndedAt!.Value,
                PlayerResultsJson = System.Text.Json.JsonSerializer.Serialize(filteredPlayers)
            };
            await gameHistoryRepo.AddAsync(history);
            _logger.LogInformation("Game history persisted for room {RoomCode}", session.RoomCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist game history for room {RoomCode}", session.RoomCode);
        }
    }

    /// <inheritdoc />
    public async Task<RejoinStateDto?> GetRejoinStateAsync(string roomCode)
    {
        var session = await _store.GetAsync(roomCode);
        if (session is null ||
            (session.State != GameState.Playing && session.State != GameState.Paused && session.State != GameState.PhaseBreak))
        {
            return null;
        }

        var players = session.Players.Select(p => ToPlayerDto(p, session)).ToList();

        var gameState = new GameStateDto(
            session.RoomCode,
            session.State.ToString(),
            players,
            session.CurrentQuestionIndex,
            session.Questions.Count,
            session.Mode.ToString());

        TurnStartedDto? turn = null;
        if (session.State != GameState.PhaseBreak)
        {
            // Segundos que le quedan al turno (0 = sala sin tiempo)
            var limit = GetTurnLimitSeconds(session);
            var remaining = 0;
            if (limit > 0)
            {
                remaining = session.State == GameState.Paused
                    ? session.PausedTimeRemaining ?? limit
                    : GetVisibleRemainingSeconds(session);
                remaining = Math.Clamp(remaining, 1, limit);
            }

            turn = BuildTurnStarted(session, remaining);
        }

        var phaseBreak = session.State == GameState.PhaseBreak ? BuildPhaseCompleted(session) : null;
        // HostInfo lleva la respuesta correcta: el hub solo se la reenvía al anfitrión.
        var hostInfo = turn is not null ? BuildHostQuestionInfo(session) : null;
        var lastResult = turn is not null && session.AwaitingNextQuestion ? session.LastTurnResult : null;
        return new RejoinStateDto(gameState, turn, session.State == GameState.Paused, phaseBreak, hostInfo, lastResult);
    }

    /// <summary>Valida el tiempo por turno: null = por defecto, 0 = sin tiempo, resto entre 5 y 120 s.</summary>
    private static int? NormalizeTurnTimeLimit(int? seconds)
    {
        if (seconds is null) return null;
        if (seconds <= 0) return 0;
        return Math.Clamp(seconds.Value, 5, 120);
    }

    /// <summary>Segundos visibles por turno (0 = sin límite).</summary>
    /// <summary>Segundos del turno en curso. La prueba de colores siempre dura lo mismo, en cualquier modo.</summary>
    private int GetTurnLimitSeconds(GameSessionDocument session) =>
        session.ColorOpen ? ColorMatch.ChallengeSeconds
        // La ocarina no tiene límite: dura hasta que alguien acierta o el anfitrión salta la pregunta.
        : session.OcarinaOpen ? 0
        : session.TurnTimeLimitSeconds ?? Math.Max(0, _options.QuestionTimeLimit - 1);

    /// <summary>
    /// Segundos visibles que le quedan a quien responde (los mismos que muestra su
    /// contador, sin el segundo de gracia). 0 si la sala no tiene tiempo o no hay deadline.
    /// </summary>
    private int GetVisibleRemainingSeconds(GameSessionDocument session)
    {
        var limit = GetTurnLimitSeconds(session);
        if (limit <= 0 || !session.TurnDeadlineUnixMs.HasValue) return 0;

        var ms = session.TurnDeadlineUnixMs.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return Math.Clamp((int)Math.Ceiling(ms / 1000.0) - 1, 0, limit);
    }

    /// <summary>Programa el deadline del turno actual si la sala tiene tiempo limitado.</summary>
    private async Task StartTurnDeadlineAsync(GameSessionDocument session)
    {
        var limit = GetTurnLimitSeconds(session);
        // Sin cuenta atrás lanzada no corre el tiempo: el plazo empieza con la cuenta atrás de verdad
        if (limit <= 0 || BuzzerWaitingForHost(session))
        {
            session.TurnDeadlineUnixMs = null;
            await _store.SaveAsync(session);
            return;
        }

        // +1 s de gracia visual; el tiempo del pulsador empieza a contar al abrirse (tras la cuenta atrás)
        session.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(limit + 1)
            .AddMilliseconds(BuzzerLockedRemainingMs(session)).ToUnixTimeMilliseconds();
        await _store.SaveAsync(session);
        await _store.ScheduleDeadlineAsync(session.RoomCode, session.TurnDeadlineUnixMs.Value, session.TurnGeneration);
    }

    /// <summary>
    /// Turno en curso tal y como lo ve la sala. <c>CurrentPlayerId</c> es quien responde
    /// ahora (el ladrón durante un robo) y <c>TurnOwnerId</c> el jugador al que le tocaba.
    /// </summary>
    private TurnStartedDto? BuildTurnStarted(GameSessionDocument session, int timeLimit)
    {
        // Con el pulsador o la prueba de colores abiertos aún no responde nadie: CurrentPlayerId = 0 hasta que haya ganador.
        var answeringPlayerId = session.GetAnsweringPlayerId() ?? (session.IsWaitingForWinner ? 0 : null);
        if (answeringPlayerId is null) return null;
        if (session.CurrentQuestionIndex >= session.Questions.Count) return null;

        var question = session.Questions[session.CurrentQuestionIndex];
        var questionDto = new QuestionDto(
            question.Id,
            question.Enunciado,
            question.Respuestas.Select(r => r.Texto).ToList(),
            question.ImagenUrl
        );

        return new TurnStartedDto(
            answeringPlayerId.Value,
            false,
            questionDto,
            timeLimit,
            question.FaseNumero,
            question.FaseNombre,
            GetTotalPhases(session),
            question.FaseColor,
            session.GetCurrentPlayerId(),
            session.StealActive,
            session.EliminatedAnswerIndexes.ToList(),
            session.DoubleOrNothingPlayers.ToList(),
            session.Bets.Select(b => new BetDto(b.UserId, b.PredictsCorrect)).ToList(),
            session.StolenById,
            session.Mode.ToString(),
            session.MarkedAnswerIndex,
            session.ComodinUsedOnQuestion,
            session.CallActive,
            question.SinTurno,
            session.BuzzerOpen,
            session.TextHiddenForPlayerId,
            question.EsColores,
            session.ColorOpen,
            session.ColorOpen ? session.ColorTarget : null,
            session.ColorOpen ? session.ColorGuesses.Select(g => g.UserId).ToList() : null,
            question.EsOcarina,
            session.OcarinaOpen,
            session.OcarinaOpen ? session.OcarinaMelody?.Select(n => new OcarinaNoteDto(n.Pitch, n.Figure.ToString())).ToList() : null,
            session.OcarinaOpen ? OcarinaListenRemainingMs(session) : 0,
            BuzzerLockedRemainingMs(session),
            BuzzerWaitingForHost(session)
        );
    }

    /// <summary>Respuesta correcta de la pregunta en curso para el anfitrión (null si no es modo presencial).</summary>
    private static HostQuestionInfoDto? BuildHostQuestionInfo(GameSessionDocument session)
    {
        if (session.CurrentQuestionIndex >= session.Questions.Count) return null;

        var question = session.Questions[session.CurrentQuestionIndex];
        var presencial = session.Mode == GameMode.Presencial;
        if (!presencial && string.IsNullOrWhiteSpace(question.Curiosidad)) return null;

        return new HostQuestionInfoDto(
            question.Id,
            presencial ? question.Respuestas.FindIndex(r => r.EsCorrecta) : null,
            question.Curiosidad);
    }

    private async Task BroadcastAnswerMarkedAsync(GameSessionDocument session)
    {
        var questionId = session.Questions[session.CurrentQuestionIndex].Id;
        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(session.RoomCode).SendAsync("AnswerMarked",
            new AnswerMarkedDto(questionId, session.MarkedAnswerIndex));
    }

    private async Task BroadcastTurnStartedAsync(GameSessionDocument session)
    {
        var turnStartedDto = BuildTurnStarted(session, GetTurnLimitSeconds(session));
        if (turnStartedDto is null) return;

        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(session.RoomCode).SendAsync("TurnStarted", turnStartedDto);

        // La respuesta correcta y la curiosidad solo le llegan al anfitrión, nunca al grupo.
        var hostInfo = BuildHostQuestionInfo(session);
        var owner = session.Players.FirstOrDefault(p => p.UserId == session.OwnerId);
        if (hostInfo is not null && !string.IsNullOrEmpty(owner?.ConnectionId))
        {
            await hubContext.Clients.Client(owner.ConnectionId).SendAsync("HostQuestionInfo", hostInfo);
        }
    }

    private async Task BroadcastTurnOutcomeAsync(string roomCode, TurnResultDto turnResult, bool timedOut)
    {
        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(roomCode).SendAsync(timedOut ? "TurnTimeout" : "TurnResult", turnResult);
    }

    private async Task BroadcastGamePausedAsync(string roomCode)
    {
        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(roomCode).SendAsync("GamePaused", new GamePausedDto(roomCode, DateTime.UtcNow));
    }

    private async Task BroadcastGameResumedAsync(string roomCode, int timeRemaining)
    {
        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(roomCode).SendAsync("GameResumed", new GameResumedDto(roomCode, timeRemaining));
    }

    private async Task<IAsyncDisposable?> AcquireRoomLockAsync(string roomCode)
    {
        var timeout = TimeSpan.FromMilliseconds(_options.RoomLockTimeoutMs);
        return await _store.AcquireLockAsync(roomCode, timeout);
    }

    private static string GenerateRoomCodeCandidate()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return string.Create(6, chars, (span, alphabet) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = alphabet[Random.Shared.Next(alphabet.Length)];
            }
        });
    }
}
