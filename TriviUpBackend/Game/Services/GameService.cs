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
    public async Task<string> CreateGameAsync(long quizId, long ownerId, string username, string connectionId)
    {
        _logger.LogInformation("Creating game room for quiz {QuizId} by owner {OwnerId} ({Username})", quizId, ownerId, username);

        using var scope = _scopeFactory.CreateScope();
        var quizRepository = scope.ServiceProvider.GetRequiredService<IQuizRepository>();
        var quiz = await quizRepository.FindByIdAsync(quizId);
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

        if (session.State != GameState.Waiting)
        {
            _logger.LogWarning("Join attempt to non-waiting room: {RoomCode}", roomCode);
            return Result.Failure<GameRoom>("Game has already started.");
        }

        if (session.Players.Count >= _options.MaxPlayersPerRoom &&
            session.Players.All(p => p.UserId != userId))
        {
            _logger.LogWarning("Join attempt to full room: {RoomCode}", roomCode);
            return Result.Failure<GameRoom>("Room is full.");
        }

        var existingPlayer = session.Players.FirstOrDefault(p => p.UserId == userId);
        if (existingPlayer != null)
        {
            if (!string.IsNullOrEmpty(existingPlayer.ConnectionId) &&
                !string.Equals(existingPlayer.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                await _store.ClearConnectionMappingAsync(existingPlayer.ConnectionId);
            }

            existingPlayer.IsConnected = true;
            existingPlayer.ConnectionId = connectionId;
            existingPlayer.Username = username;
        }
        else
        {
            session.Players.Add(new PlayerDocument
            {
                UserId = userId,
                Username = username,
                ConnectionId = connectionId,
                IsConnected = true,
                IsOwner = false
            });
        }

        await _store.SaveAsync(session);
        await _store.SetConnectionMappingAsync(connectionId, userId, roomCode);
        await _store.SetUserRoomAsync(userId, roomCode);

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

            if (player.IsOwner && session.State == GameState.Waiting)
            {
                var newOwner = session.Players.FirstOrDefault(p => p.UserId != userId && p.IsConnected);
                if (newOwner != null)
                {
                    newOwner.IsOwner = true;
                    session.OwnerId = newOwner.UserId;
                    player.IsOwner = false;
                    _logger.LogInformation("Owner transferred to {NewOwnerId} in room {RoomCode}", newOwner.UserId, roomCode);
                }
            }

            if (!string.IsNullOrEmpty(player.ConnectionId))
            {
                await _store.ClearConnectionMappingAsync(player.ConnectionId);
            }
        }

        await _store.ClearUserRoomAsync(userId);
        await _store.SaveAsync(session);
        _logger.LogInformation("User {UserId} left room {RoomCode}", userId, roomCode);
        return false;
    }

    /// <summary>
    /// Cierra la sala por completo: limpia el mapeo de todos los jugadores, borra la sesión
    /// del store, los saca del grupo de SignalR y difunde <c>RoomClosed</c>.
    /// </summary>
    private async Task CloseRoomAsync(GameSessionDocument session)
    {
        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

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

        await hubContext.Clients.Group(session.RoomCode).SendAsync("RoomClosed", new RoomClosedDto(session.RoomCode, "OWNER_LEFT"));

        _logger.LogInformation("Room {RoomCode} closed because owner left explicitly while waiting", session.RoomCode);
    }

    /// <inheritdoc />
    public async Task<GameRoom?> StartGameAsync(string roomCode, long userId)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return null;

        var session = await _store.GetAsync(roomCode);
        if (session is null) return null;

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

        var activePlayers = session.Players.Where(p => p.IsConnected).ToList();
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
        questions = questions.OrderBy(_ => random.Next()).ToList();
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
        session.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(_options.QuestionTimeLimit).ToUnixTimeMilliseconds();

        await _store.SaveAsync(session);
        await _store.ScheduleDeadlineAsync(roomCode, session.TurnDeadlineUnixMs.Value, session.TurnGeneration);

        _logger.LogInformation("Game started in room {RoomCode} with {QuestionCount} questions", roomCode, questions.Count);

        await BroadcastTurnStartedAsync(session);
        return GameSessionMapper.ToRoom(session);
    }

    /// <inheritdoc />
    public async Task<TurnResultDto?> SubmitAnswerAsync(string roomCode, long userId, long questionId, int answerIndex, int timeRemaining)
    {
        await using var roomLock = await AcquireRoomLockAsync(roomCode);
        if (roomLock is null) return null;

        var session = await _store.GetAsync(roomCode);
        if (session is null || session.State != GameState.Playing) return null;

        var currentPlayerId = session.GetCurrentPlayerId();
        if (currentPlayerId != userId)
        {
            _logger.LogWarning("User {UserId} attempted to answer but it's not their turn in room {RoomCode}", userId, roomCode);
            return null;
        }

        var player = session.Players.FirstOrDefault(p => p.UserId == userId);
        if (player is null) return null;

        var question = session.Questions.FirstOrDefault(q => q.Id == questionId);
        if (question is null) return null;

        await _store.ClearDeadlineAsync(roomCode);

        var correctAnswer = question.Respuestas.FirstOrDefault(r => r.EsCorrecta);
        var correctAnswerIndex = correctAnswer != null ? question.Respuestas.IndexOf(correctAnswer) : -1;
        var selectedAnswer = question.Respuestas.ElementAtOrDefault(answerIndex);
        var isCorrect = correctAnswer != null && selectedAnswer != null &&
                        string.Equals(correctAnswer.Texto, selectedAnswer.Texto, StringComparison.OrdinalIgnoreCase);

        var pointsEarned = 0;
        if (isCorrect)
        {
            pointsEarned = _options.BasePoints + (timeRemaining * _options.TimeBonusMultiplier);
            pointsEarned = Math.Min(pointsEarned, _options.BasePoints + _options.MaxTimeBonus);
            player.Score += pointsEarned;
            player.CorrectAnswers++;
        }
        else
        {
            player.WrongAnswers++;
        }

        var turnResult = new TurnResultDto(userId, isCorrect, correctAnswerIndex, pointsEarned, player.Score);
        await BroadcastTurnResultAsync(roomCode, turnResult);
        await AdvanceToNextTurnAsync(session);
        return turnResult;
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

        var currentPlayerId = session.GetCurrentPlayerId();
        if (currentPlayerId is null)
        {
            await _store.ClearDeadlineAsync(roomCode);
            return;
        }

        var player = session.Players.FirstOrDefault(p => p.UserId == currentPlayerId);
        if (player is null)
        {
            await _store.ClearDeadlineAsync(roomCode);
            return;
        }

        player.WrongAnswers++;

        if (session.CurrentQuestionIndex >= session.Questions.Count)
        {
            await EndGameAsync(session);
            return;
        }

        var question = session.Questions[session.CurrentQuestionIndex];
        var correctAnswer = question.Respuestas.FirstOrDefault(r => r.EsCorrecta);
        var correctAnswerIndex = correctAnswer != null ? question.Respuestas.IndexOf(correctAnswer) : -1;

        var turnResult = new TurnResultDto(currentPlayerId.Value, false, correctAnswerIndex, 0, player.Score);
        await _store.ClearDeadlineAsync(roomCode);
        await BroadcastTurnTimeoutAsync(roomCode, turnResult);
        await AdvanceToNextTurnAsync(session);
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
        else if (session.TurnStartedAt.HasValue)
        {
            var elapsed = (DateTime.UtcNow - session.TurnStartedAt.Value).TotalSeconds;
            session.PausedTimeRemaining = Math.Max(0, _options.QuestionTimeLimit - (int)elapsed);
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

        var timeToResume = session.PausedTimeRemaining ?? _options.QuestionTimeLimit;
        session.PausedTimeRemaining = null;
        session.State = GameState.Playing;
        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;
        session.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(timeToResume).ToUnixTimeMilliseconds();

        await _store.SaveAsync(session);
        await _store.ScheduleDeadlineAsync(roomCode, session.TurnDeadlineUnixMs.Value, session.TurnGeneration);
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
        session.Players.Remove(playerToKick);
        session.TurnQueue.RemoveAll(id => id == playerIdToKick);

        await _store.ClearUserRoomAsync(playerIdToKick);
        if (!string.IsNullOrEmpty(connectionIdToRemove))
        {
            await _store.ClearConnectionMappingAsync(connectionIdToRemove);
        }

        await _store.SaveAsync(session);

        _logger.LogInformation("Player {PlayerIdToKick} kicked from room {RoomCode} by owner {OwnerId}",
            playerIdToKick, roomCode, ownerId);

        return Result.Success<string?>(connectionIdToRemove);
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
        session.CurrentQuestionIndex++;
        if (session.CurrentQuestionIndex >= session.Questions.Count)
        {
            await EndGameAsync(session);
            return;
        }

        var nextPlayerId = session.RotateTurn();
        if (nextPlayerId is null)
        {
            await EndGameAsync(session);
            return;
        }

        session.TurnStartedAt = DateTime.UtcNow;
        session.TurnGeneration++;
        session.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(_options.QuestionTimeLimit).ToUnixTimeMilliseconds();

        await _store.SaveAsync(session);
        await _store.ScheduleDeadlineAsync(session.RoomCode, session.TurnDeadlineUnixMs.Value, session.TurnGeneration);
        await BroadcastTurnStartedAsync(session);
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
            .Where(p => p.UserId != session.OwnerId)
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

    private async Task BroadcastTurnStartedAsync(GameSessionDocument session)
    {
        var currentPlayerId = session.GetCurrentPlayerId();
        if (currentPlayerId is null) return;
        if (session.CurrentQuestionIndex >= session.Questions.Count) return;

        var question = session.Questions[session.CurrentQuestionIndex];
        var questionDto = new QuestionDto(
            question.Id,
            question.Enunciado,
            question.Respuestas.Select(r => r.Texto).ToList(),
            question.ImagenUrl
        );

        var turnStartedDto = new TurnStartedDto(
            currentPlayerId.Value,
            false,
            questionDto,
            _options.QuestionTimeLimit - 1
        );

        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(session.RoomCode).SendAsync("TurnStarted", turnStartedDto);
    }

    private async Task BroadcastTurnResultAsync(string roomCode, TurnResultDto turnResult)
    {
        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(roomCode).SendAsync("TurnResult", turnResult);
    }

    private async Task BroadcastTurnTimeoutAsync(string roomCode, TurnResultDto turnResult)
    {
        using var scope = _scopeFactory.CreateScope();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();
        await hubContext.Clients.Group(roomCode).SendAsync("TurnTimeout", turnResult);
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
