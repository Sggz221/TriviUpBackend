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
    public async Task<string> CreateGameAsync(long quizId, long ownerId, string username, string connectionId, int? turnTimeLimitSeconds = null)
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
                TurnTimeLimitSeconds = NormalizeTurnTimeLimit(turnTimeLimitSeconds),
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
        var playersList = session.Players.Select(ToPlayerDto).ToList();

        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(session.RoomCode).SendAsync("PlayersList", playersList);
    }

    private static PlayerDto ToPlayerDto(PlayerDocument p) => new(
        p.UserId, p.Username, p.Score, p.CorrectAnswers, p.WrongAnswers, false, p.IsOwner, p.IsConnected, p.IsSpectator,
        ComodinNames(p.AvailableComodines()));

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

        List<Pregunta> questions;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
            questions = await repository.GetQuestionsWithAnswersAsync(session.QuizId);
        }

        if (questions is null || questions.Count == 0)
        {
            _logger.LogWarning("No questions found for quiz {QuizId} in room {RoomCode}", session.QuizId, roomCode);
            session.State = GameState.Waiting;
            await _store.SaveAsync(session);
            return null;
        }

        var random = new Random();
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

        session.Questions = GameSessionMapper.SnapshotQuestions(questions);
        session.TurnQueue = playerIds;
        session.State = GameState.Playing;
        session.StartedAt = DateTime.UtcNow;
        session.CurrentQuestionIndex = 0;
        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;

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

        var correctAnswer = question.Respuestas.FirstOrDefault(r => r.EsCorrecta);
        var selectedAnswer = question.Respuestas[answerIndex];
        var isCorrect = correctAnswer != null &&
                        string.Equals(correctAnswer.Texto, selectedAnswer.Texto, StringComparison.OrdinalIgnoreCase);

        return await ResolveAnswerAsync(session, player, question, isCorrect, remainingSeconds, timedOut: false);
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
        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;
        await _store.ClearDeadlineAsync(session.RoomCode);
        await StartTurnDeadlineAsync(session);
        await BroadcastTurnStartedAsync(session);
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

        if (player.UsedComodines.Contains(tipo))
        {
            return Result.Failure<ComodinUsedDto>("Ya has usado ese comodín.");
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
        long? stolenFrom = null;

        switch (tipo)
        {
            case ComodinTipo.Ruleta:
            {
                var candidates = Enumerable.Range(0, question.Respuestas.Count)
                    .Where(i => !question.Respuestas[i].EsCorrecta && !session.EliminatedAnswerIndexes.Contains(i))
                    .ToList();
                var rolled = ComodinReglas.TirarRuleta(Random.Shared);
                eliminated = candidates.OrderBy(_ => Random.Shared.Next()).Take(rolled).OrderBy(i => i).ToList();
                ruletaResultado = eliminated.Count;
                session.EliminatedAnswerIndexes.AddRange(eliminated);
                break;
            }
            case ComodinTipo.DobleONada:
                session.DoubleOrNothingPlayers.Add(userId);
                break;
            case ComodinTipo.Robo:
                if (session.StolenById.HasValue)
                {
                    return Result.Failure<ComodinUsedDto>("Esta pregunta ya ha sido robada.");
                }
                if (session.Bets.Any(b => b.UserId == userId))
                {
                    return Result.Failure<ComodinUsedDto>("No puedes robar una pregunta sobre la que has apostado.");
                }
                stolenFrom = turnOwnerId;
                session.StolenById = userId;
                session.StealActive = true;
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
        }

        player.UsedComodines.Add(tipo);

        var dto = new ComodinUsedDto(
            userId, player.Username, tipo.ToString(), question.Id, ComodinNames(player.AvailableComodines()),
            eliminated, ruletaResultado, tipo == ComodinTipo.Apuesta ? predictsCorrect : null, stolenFrom);

        using (var scope = _scopeFactory.CreateScope())
        {
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
            await hubContext.Clients.Group(roomCode).SendAsync("ComodinUsed", dto);
        }

        if (tipo == ComodinTipo.Robo)
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
        else if (session.TurnStartedAt.HasValue && GetTurnLimitSeconds(session) > 0)
        {
            var elapsed = (DateTime.UtcNow - session.TurnStartedAt.Value).TotalSeconds;
            session.PausedTimeRemaining = Math.Max(0, GetTurnLimitSeconds(session) + 1 - (int)elapsed);
        }

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

        var hasLimit = GetTurnLimitSeconds(session) > 0;
        var timeToResume = hasLimit ? (session.PausedTimeRemaining ?? GetTurnLimitSeconds(session) + 1) : 0;
        session.PausedTimeRemaining = null;
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
        session.Players.Remove(playerToKick);
        session.TurnQueue.RemoveAll(id => id == playerIdToKick);
        session.Bets.RemoveAll(b => b.UserId == playerIdToKick);

        await _store.ClearUserRoomAsync(playerIdToKick);
        if (!string.IsNullOrEmpty(connectionIdToRemove))
        {
            await _store.ClearConnectionMappingAsync(connectionIdToRemove);
        }

        // Expulsado en mitad de un robo: la pregunta vuelve al jugador original con el tiempo completo.
        if (wasActiveThief && session.State == GameState.Playing)
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
        var nextPlayerId = session.RotateTurn();
        if (nextPlayerId is null)
        {
            await EndGameAsync(session);
            return;
        }

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
        var players = session.Players.Select(ToPlayerDto).ToList();

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

        var players = session.Players.Select(ToPlayerDto).ToList();

        var gameState = new GameStateDto(
            session.RoomCode,
            session.State.ToString(),
            players,
            session.CurrentQuestionIndex,
            session.Questions.Count);

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
        return new RejoinStateDto(gameState, turn, session.State == GameState.Paused, phaseBreak);
    }

    /// <summary>Valida el tiempo por turno: null = por defecto, 0 = sin tiempo, resto entre 5 y 120 s.</summary>
    private static int? NormalizeTurnTimeLimit(int? seconds)
    {
        if (seconds is null) return null;
        if (seconds <= 0) return 0;
        return Math.Clamp(seconds.Value, 5, 120);
    }

    /// <summary>Segundos visibles por turno (0 = sin límite).</summary>
    private int GetTurnLimitSeconds(GameSessionDocument session) =>
        session.TurnTimeLimitSeconds ?? Math.Max(0, _options.QuestionTimeLimit - 1);

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
        if (limit <= 0)
        {
            session.TurnDeadlineUnixMs = null;
            await _store.SaveAsync(session);
            return;
        }

        // +1 s de gracia visual
        session.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(limit + 1).ToUnixTimeMilliseconds();
        await _store.SaveAsync(session);
        await _store.ScheduleDeadlineAsync(session.RoomCode, session.TurnDeadlineUnixMs.Value, session.TurnGeneration);
    }

    /// <summary>
    /// Turno en curso tal y como lo ve la sala. <c>CurrentPlayerId</c> es quien responde
    /// ahora (el ladrón durante un robo) y <c>TurnOwnerId</c> el jugador al que le tocaba.
    /// </summary>
    private TurnStartedDto? BuildTurnStarted(GameSessionDocument session, int timeLimit)
    {
        var answeringPlayerId = session.GetAnsweringPlayerId();
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
            session.StolenById
        );
    }

    private async Task BroadcastTurnStartedAsync(GameSessionDocument session)
    {
        var turnStartedDto = BuildTurnStarted(session, GetTurnLimitSeconds(session));
        if (turnStartedDto is null) return;

        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(session.RoomCode).SendAsync("TurnStarted", turnStartedDto);
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
