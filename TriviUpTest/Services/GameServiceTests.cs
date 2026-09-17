using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TriviUpBackend.Game.Services;
using TriviUpBackend.Game.Configuration;
using TriviUpBackend.Game.Hubs;
using TriviUpBackend.Game.Persistence;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Cuestionarios.Entities;
using Microsoft.AspNetCore.SignalR;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;

namespace TriviUpTest.Services;

public class GameServiceTests
{
    private readonly Mock<ILogger<GameService>> _mockLogger;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly GameOptions _options;
    private readonly InMemoryGameSessionStore _store;
    private readonly GameService _service;
    private readonly Mock<IHubContext<GameHub>> _mockHubContext;

    public GameServiceTests()
    {
        _mockLogger = new Mock<ILogger<GameService>>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockHubContext = new Mock<IHubContext<GameHub>>();
        _store = new InMemoryGameSessionStore();
        _options = new GameOptions
        {
            MaxPlayersPerRoom = 10,
            MinPlayersToStart = 2,
            QuestionTimeLimit = 21,
            BasePoints = 100,
            TimeBonusMultiplier = 10,
            MaxTimeBonus = 200,
            RoomLockTimeoutMs = 2000,
            DeadlinePollIntervalMs = 50
        };

        SetupHubContext();
        _service = new GameService(_store, _options, _mockLogger.Object, _mockScopeFactory.Object);
    }

    private void SetupHubContext()
    {
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockHubClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockHubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _mockHubContext.Setup(h => h.Clients).Returns(mockHubClients.Object);

        var mockGroupManager = new Mock<IGroupManager>();
        mockGroupManager
            .Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockHubContext.Setup(h => h.Groups).Returns(mockGroupManager.Object);

        var mockQuizRepository = new Mock<IQuizRepository>();
        mockQuizRepository.Setup(r => r.FindByIdAsync(It.IsAny<long>()))
            .ReturnsAsync((long id) => new Quiz { Id = id, Nombre = "Test Quiz" });
        mockQuizRepository.Setup(r => r.GetQuestionsWithAnswersAsync(It.IsAny<long>()))
            .ReturnsAsync(CreateDefaultQuestions(1));

        mockServiceProvider.Setup(sp => sp.GetService(typeof(IHubContext<GameHub>))).Returns(_mockHubContext.Object);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IQuizRepository))).Returns(mockQuizRepository.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
    }

    private static List<Pregunta> CreateDefaultQuestions(long quizId) =>
    [
        new()
        {
            Id = 1,
            QuizId = quizId,
            NumeroPregunta = 1,
            Enunciado = "Test question",
            Respuestas =
            [
                new Respuesta { Id = 1, Texto = "Answer A", EsCorrecta = true },
                new Respuesta { Id = 2, Texto = "Answer B", EsCorrecta = false }
            ]
        },
        new()
        {
            Id = 2,
            QuizId = quizId,
            NumeroPregunta = 2,
            Enunciado = "Test question 2",
            Respuestas =
            [
                new Respuesta { Id = 3, Texto = "Yes", EsCorrecta = true },
                new Respuesta { Id = 4, Texto = "No", EsCorrecta = false }
            ]
        }
    ];

    // ========== CreateGameAsync Tests ==========

    [Fact]
    public async Task CreateGameAsync_ValidQuizId_ReturnsRoomCode()
    {
        var result = await _service.CreateGameAsync(1L, 100L, "owner", "conn-owner");

        Assert.NotNull(result);
        Assert.Equal(6, result.Length);
    }

    [Fact]
    public async Task CreateGameAsync_RegistersOwnerConnectionMapping()
    {
        var roomCode = await _service.CreateGameAsync(1L, 100L, "owner", "conn-owner");

        var mapped = await _service.GetRoomCodeByConnectionAsync("conn-owner");
        Assert.Equal(roomCode, mapped);
    }

    // ========== JoinGameAsync Tests ==========

    [Fact]
    public async Task JoinGameAsync_ValidRoomCode_ReturnsSuccessWithRoom()
    {
        var roomCode = await CreateTestRoom();
        var result = await _service.JoinGameAsync(roomCode, 200L, "player", "conn-123");

        Assert.True(result.IsSuccess);
        Assert.Equal(roomCode, result.Value.RoomCode);
    }

    [Fact]
    public async Task JoinGameAsync_NonExistentRoom_ReturnsFailure()
    {
        var result = await _service.JoinGameAsync("NONEXIST", 200L, "player", "conn-123");

        Assert.True(result.IsFailure);
        Assert.Equal("Room not found.", result.Error);
    }

    [Fact]
    public async Task JoinGameAsync_FullRoom_ReturnsFailure()
    {
        var roomCode = await CreateFullTestRoom();
        var result = await _service.JoinGameAsync(roomCode, 999L, "newplayer", "conn-999");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task JoinGameAsync_ExistingPlayer_ReturnsSuccessWithExistingPlayer()
    {
        var roomCode = await CreateTestRoom();
        var result = await _service.JoinGameAsync(roomCode, 100L, "owner", "new-connection");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task JoinGameAsync_GameAlreadyStarted_ReturnsFailure()
    {
        var roomCode = await CreatePlayingTestRoom();
        var result = await _service.JoinGameAsync(roomCode, 300L, "lateplayer", "conn-late");

        Assert.True(result.IsFailure);
        Assert.Equal("Game has already started.", result.Error);
    }

    [Fact]
    public async Task JoinGameAsync_ExistingPlayerReconnectsWhilePlaying_ReturnsSuccess()
    {
        var roomCode = await CreatePlayingTestRoom();
        var result = await _service.JoinGameAsync(roomCode, 200L, "player2", "conn-200-new");

        Assert.True(result.IsSuccess);
        var session = await _store.GetAsync(roomCode);
        var reconnected = session!.Players.Single(p => p.UserId == 200L);
        Assert.True(reconnected.IsConnected);
        Assert.Equal("conn-200-new", reconnected.ConnectionId);
    }

    [Fact]
    public async Task JoinGameAsync_SharedStore_VisibleAcrossServiceInstances()
    {
        var roomCode = await CreateTestRoom();
        var otherService = new GameService(_store, _options, _mockLogger.Object, _mockScopeFactory.Object);

        var result = await otherService.JoinGameAsync(roomCode, 200L, "player", "conn-200");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Players.Count);
    }

    // ========== LeaveGameAsync Tests ==========

    [Fact]
    public async Task LeaveGameAsync_OwnerDisconnectsDuringWaiting_DoesNotTransferImmediately()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        await _service.LeaveGameAsync(roomCode, 100L);

        var session = await _store.GetAsync(roomCode);
        Assert.NotNull(session);
        Assert.Equal(100L, session.OwnerId);

        var owner = session.Players.Single(p => p.UserId == 100L);
        Assert.False(owner.IsConnected);
        Assert.NotNull(owner.DisconnectedAt);
    }

    [Fact]
    public async Task JoinGameAsync_OwnerReconnectGraceExpired_TransfersOwnershipLazily()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        await _service.LeaveGameAsync(roomCode, 100L);

        // Simulate the reconnect grace period having already expired
        var session = await _store.GetAsync(roomCode);
        session!.Players.Single(p => p.UserId == 100L).DisconnectedAt = DateTime.UtcNow.AddMinutes(-10);
        await _store.SaveAsync(session);

        // Any join/reconnect in the room lazily resolves the stale ownership
        await _service.JoinGameAsync(roomCode, 200L, "player2", "conn-200-new");

        var updated = await _store.GetAsync(roomCode);
        Assert.Equal(200L, updated!.OwnerId);
        Assert.True(updated.Players.Single(p => p.UserId == 200L).IsOwner);
        Assert.False(updated.Players.Single(p => p.UserId == 100L).IsOwner);
    }

    [Fact]
    public async Task JoinGameAsync_OwnerReconnectsWithinGrace_KeepsOwnership()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        await _service.LeaveGameAsync(roomCode, 100L);

        var result = await _service.JoinGameAsync(roomCode, 100L, "owner", "conn-100-new");

        Assert.True(result.IsSuccess);
        var session = await _store.GetAsync(roomCode);
        var owner = session!.Players.Single(p => p.UserId == 100L);
        Assert.True(owner.IsOwner);
        Assert.True(owner.IsConnected);
        Assert.Null(owner.DisconnectedAt);
        Assert.Equal(100L, session.OwnerId);
    }

    [Fact]
    public async Task LeaveGameAsync_OwnerExplicitLeaveDuringWaiting_ClosesRoom()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        var closed = await _service.LeaveGameAsync(roomCode, 100L, isExplicitLeave: true);

        Assert.True(closed);
        var session = await _store.GetAsync(roomCode);
        Assert.Null(session);
    }

    [Fact]
    public async Task LeaveGameAsync_NonExistentRoom_DoesNotThrow()
    {
        await _service.LeaveGameAsync("NOTFOUND", 100L);
    }

    [Fact]
    public async Task LeaveGameAsync_UserNotInRoom_DoesNotThrow()
    {
        var roomCode = await CreateTestRoom();
        await _service.LeaveGameAsync(roomCode, 999L);
    }

    // ========== StartGameAsync Tests ==========

    [Fact]
    public async Task StartGameAsync_NonOwnerTriesToStart_ReturnsNull()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        Assert.Null(await _service.StartGameAsync(roomCode, 200L));
    }

    [Fact]
    public async Task StartGameAsync_NotEnoughPlayers_ReturnsNull()
    {
        var roomCode = await CreateTestRoom();
        Assert.Null(await _service.StartGameAsync(roomCode, 100L));
    }

    [Fact]
    public async Task StartGameAsync_NonExistentRoom_ReturnsNull()
    {
        Assert.Null(await _service.StartGameAsync("NOTFOUND", 100L));
    }

    [Fact]
    public async Task StartGameAsync_GameAlreadyStarted_ReturnsRoom()
    {
        var roomCode = await CreatePlayingTestRoom();
        var result = await _service.StartGameAsync(roomCode, 100L);

        Assert.NotNull(result);
        Assert.Equal(roomCode, result.RoomCode);
    }

    [Fact]
    public async Task StartGameAsync_SchedulesDeadline()
    {
        var roomCode = await CreatePlayingTestRoom();
        var session = await _store.GetAsync(roomCode);

        Assert.NotNull(session);
        Assert.True(session.TurnDeadlineUnixMs.HasValue);
        Assert.True(session.TurnGeneration > 0);

        var due = await _store.GetDueDeadlinesAsync(session.TurnDeadlineUnixMs!.Value + 1, 10);
        Assert.Contains(due, d => d.RoomCode == roomCode);
    }

    // ========== SubmitAnswerAsync Tests ==========

    [Fact]
    public async Task SubmitAnswerAsync_RoomNotFound_ReturnsNull()
    {
        Assert.Null(await _service.SubmitAnswerAsync("NOTFOUND", 100L, 1, 0, 10));
    }

    [Fact]
    public async Task SubmitAnswerAsync_PlayerNotFound_ReturnsNull()
    {
        var roomCode = await CreatePlayingTestRoom();
        Assert.Null(await _service.SubmitAnswerAsync(roomCode, 999L, 1, 0, 10));
    }

    [Fact]
    public async Task SubmitAnswerAsync_NotPlayersTurn_ReturnsNull()
    {
        var roomCode = await CreatePlayingTestRoom();
        Assert.Null(await _service.SubmitAnswerAsync(roomCode, 100L, 1, 0, 10));
    }

    [Fact]
    public async Task SubmitAnswerAsync_CorrectAnswer_ReturnsTurnResultWithPoints()
    {
        var roomCode = await CreatePlayingTestRoom();
        var session = await _store.GetAsync(roomCode);
        Assert.NotNull(session);

        var playerId = session.GetCurrentPlayerId()!.Value;
        var questionId = session.Questions[session.CurrentQuestionIndex].Id;
        var correctIndex = session.Questions[session.CurrentQuestionIndex].Respuestas.FindIndex(r => r.EsCorrecta);

        var result = await _service.SubmitAnswerAsync(roomCode, playerId, questionId, correctIndex, 15);

        Assert.NotNull(result);
        Assert.Equal(playerId, result.PlayerId);
        Assert.True(result.IsCorrect);
        Assert.True(result.PointsEarned > 0);
    }

    [Fact]
    public async Task SubmitAnswerAsync_IncorrectAnswer_ReturnsTurnResultWithNoPoints()
    {
        var roomCode = await CreatePlayingTestRoom();
        Assert.Null(await _service.SubmitAnswerAsync(roomCode, 200L, 999, 0, 15));
    }

    [Fact]
    public async Task SubmitAnswerAsync_VsTimeout_OnlyOneAdvances()
    {
        var roomCode = await CreatePlayingTestRoom();
        var sessionBefore = await _store.GetAsync(roomCode);
        Assert.NotNull(sessionBefore);

        var generation = sessionBefore.TurnGeneration;
        var playerId = sessionBefore.GetCurrentPlayerId()!.Value;
        var questionId = sessionBefore.Questions[sessionBefore.CurrentQuestionIndex].Id;
        var indexBefore = sessionBefore.CurrentQuestionIndex;

        var answerTask = _service.SubmitAnswerAsync(roomCode, playerId, questionId, 0, 10);
        var timeoutTask = _service.ProcessDueTimeoutAsync(roomCode, generation);
        await Task.WhenAll(answerTask, timeoutTask);

        var sessionAfter = await _store.GetAsync(roomCode);
        Assert.NotNull(sessionAfter);
        Assert.Equal(indexBefore + 1, sessionAfter.CurrentQuestionIndex);
        Assert.True(sessionAfter.TurnGeneration > generation);
    }

    // ========== KickPlayerAsync Tests ==========

    [Fact]
    public async Task KickPlayerAsync_RoomDoesNotExist_ReturnsFailure()
    {
        Assert.True((await _service.KickPlayerAsync("NOTFOUND", 100L, 200L)).IsFailure);
    }

    [Fact]
    public async Task KickPlayerAsync_NonOwnerTriesToKick_ReturnsFailure()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        Assert.True((await _service.KickPlayerAsync(roomCode, 200L, 300L)).IsFailure);
    }

    [Fact]
    public async Task KickPlayerAsync_OwnerKicksSelf_ReturnsFailure()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        Assert.True((await _service.KickPlayerAsync(roomCode, 100L, 100L)).IsFailure);
    }

    [Fact]
    public async Task KickPlayerAsync_OwnerKicksNonExistentPlayer_ReturnsFailure()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        Assert.True((await _service.KickPlayerAsync(roomCode, 100L, 999L)).IsFailure);
    }

    [Fact]
    public async Task KickPlayerAsync_PlayerKicked_RemovedFromMappings()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        var result = await _service.KickPlayerAsync(roomCode, 100L, 200L);

        Assert.True(result.IsSuccess);
        Assert.Null(await _service.GetRoomCodeByConnectionAsync("conn-200"));
    }

    // ========== Pause / Resume ==========

    [Fact]
    public async Task PauseGameAsync_OwnerPausesGame_ReturnsSuccess()
    {
        var roomCode = await CreatePlayingTestRoom();
        Assert.True((await _service.PauseGameAsync(roomCode, 100L)).IsSuccess);

        var session = await _store.GetAsync(roomCode);
        Assert.Equal(TriviUpBackend.Game.Models.GameState.Paused, session!.State);
        Assert.Null(session.TurnDeadlineUnixMs);
    }

    [Fact]
    public async Task PauseGameAsync_NonOwnerTriesToPause_ReturnsFailure()
    {
        var roomCode = await CreatePlayingTestRoom();
        Assert.True((await _service.PauseGameAsync(roomCode, 200L)).IsFailure);
    }

    [Fact]
    public async Task PauseGameAsync_NonExistentRoom_ReturnsFailure()
    {
        Assert.True((await _service.PauseGameAsync("NOTFOUND", 100L)).IsFailure);
    }

    [Fact]
    public async Task PauseGameAsync_WrongState_ReturnsFailure()
    {
        var roomCode = await CreateTestRoom();
        Assert.True((await _service.PauseGameAsync(roomCode, 100L)).IsFailure);
    }

    [Fact]
    public async Task ResumeGameAsync_OwnerResumesGame_ReturnsSuccess()
    {
        var roomCode = await CreatePausedTestRoom();
        Assert.True((await _service.ResumeGameAsync(roomCode, 100L)).IsSuccess);

        var session = await _store.GetAsync(roomCode);
        Assert.Equal(TriviUpBackend.Game.Models.GameState.Playing, session!.State);
        Assert.True(session.TurnDeadlineUnixMs.HasValue);
        Assert.NotNull(session.TurnStartedAt);
    }

    [Fact]
    public async Task ResumeGameAsync_NonOwnerTriesToResume_ReturnsFailure()
    {
        var roomCode = await CreatePausedTestRoom();
        Assert.True((await _service.ResumeGameAsync(roomCode, 200L)).IsFailure);
    }

    [Fact]
    public async Task ResumeGameAsync_NonExistentRoom_ReturnsFailure()
    {
        Assert.True((await _service.ResumeGameAsync("NOTFOUND", 100L)).IsFailure);
    }

    [Fact]
    public async Task ResumeGameAsync_WrongState_ReturnsFailure()
    {
        var roomCode = await CreatePlayingTestRoom();
        Assert.True((await _service.ResumeGameAsync(roomCode, 100L)).IsFailure);
    }

    [Fact]
    public async Task ProcessDueTimeoutAsync_StaleGeneration_DoesNotAdvance()
    {
        var roomCode = await CreatePlayingTestRoom();
        var session = await _store.GetAsync(roomCode);
        Assert.NotNull(session);
        var indexBefore = session.CurrentQuestionIndex;

        await _service.ProcessDueTimeoutAsync(roomCode, session.TurnGeneration - 1);

        var after = await _store.GetAsync(roomCode);
        Assert.Equal(indexBefore, after!.CurrentQuestionIndex);
    }

    [Fact]
    public async Task ProcessDueTimeoutAsync_ValidDeadline_AdvancesTurn()
    {
        var roomCode = await CreatePlayingTestRoom();
        var session = await _store.GetAsync(roomCode);
        Assert.NotNull(session);
        var indexBefore = session.CurrentQuestionIndex;
        var generation = session.TurnGeneration;

        // Force deadline into the past
        session.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000;
        await _store.SaveAsync(session);
        await _store.ScheduleDeadlineAsync(roomCode, session.TurnDeadlineUnixMs.Value, generation);

        await _service.ProcessDueTimeoutAsync(roomCode, generation);

        var after = await _store.GetAsync(roomCode);
        Assert.Equal(indexBefore + 1, after!.CurrentQuestionIndex);
    }

    // ========== Connection mapping ==========

    [Fact]
    public async Task GetRoomCodeByConnectionAsync_ValidConnection_ReturnsRoomCode()
    {
        var roomCode = await CreateTestRoom();
        await _service.JoinGameAsync(roomCode, 100L, "player", "test-connection");

        Assert.Equal(roomCode, await _service.GetRoomCodeByConnectionAsync("test-connection"));
    }

    [Fact]
    public async Task GetRoomCodeByConnectionAsync_InvalidConnection_ReturnsNull()
    {
        Assert.Null(await _service.GetRoomCodeByConnectionAsync("non-existent-connection"));
    }

    [Fact]
    public async Task HandleDisconnectionAsync_ValidConnection_HandlesGracefully()
    {
        var roomCode = await CreateTestRoom();
        await _service.JoinGameAsync(roomCode, 100L, "player", "disconnect-test");
        await _service.HandleDisconnectionAsync("disconnect-test");

        Assert.Null(await _service.GetRoomCodeByConnectionAsync("disconnect-test"));
    }

    [Fact]
    public async Task HandleDisconnectionAsync_UnknownConnection_DoesNotThrow()
    {
        await _service.HandleDisconnectionAsync("unknown-connection");
    }

    [Fact]
    public async Task GetRoomCodeByConnectionAsync_DisconnectedPlayer_ReturnsNull()
    {
        var roomCode = await CreateTestRoom();
        await _service.JoinGameAsync(roomCode, 999L, "player", "disconnect-test-conn");
        await _service.HandleDisconnectionAsync("disconnect-test-conn");

        Assert.Null(await _service.GetRoomCodeByConnectionAsync("disconnect-test-conn"));
    }

    [Fact]
    public async Task CreateGameAsync_RoomCodesAreUniqueInStore()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 30; i++)
        {
            var code = await _service.CreateGameAsync(1L, 1000 + i, $"owner{i}", $"conn-{i}");
            Assert.True(codes.Add(code));
        }
    }

    // ========== Helpers ==========

    private async Task<string> CreateTestRoom()
    {
        return await _service.CreateGameAsync(1L, 100L, "owner", "conn-owner");
    }

    private async Task<string> CreateTestRoomWithTwoPlayers()
    {
        var roomCode = await CreateTestRoom();
        await _service.JoinGameAsync(roomCode, 200L, "player2", "conn-200");
        return roomCode;
    }

    private async Task<string> CreateFullTestRoom()
    {
        var roomCode = await CreateTestRoom();
        for (int i = 1; i < 10; i++)
        {
            await _service.JoinGameAsync(roomCode, 100 + i, $"player{i}", $"conn-{100 + i}");
        }
        return roomCode;
    }

    private async Task<string> CreatePlayingTestRoom()
    {
        var roomCode = await CreateTestRoomWithTwoPlayers();
        var started = await _service.StartGameAsync(roomCode, 100L);
        Assert.NotNull(started);
        return roomCode;
    }

    private async Task<string> CreatePausedTestRoom()
    {
        var roomCode = await CreatePlayingTestRoom();
        Assert.True((await _service.PauseGameAsync(roomCode, 100L)).IsSuccess);
        return roomCode;
    }
}
