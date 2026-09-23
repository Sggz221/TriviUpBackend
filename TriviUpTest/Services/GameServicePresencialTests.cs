using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Game.Configuration;
using TriviUpBackend.Game.DTOs;
using TriviUpBackend.Game.Hubs;
using TriviUpBackend.Game.Models;
using TriviUpBackend.Game.Persistence;
using TriviUpBackend.Game.Services;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;

namespace TriviUpTest.Services;

/// <summary>Modo presencial: el anfitrión marca, confirma y pasa de pregunta por los jugadores.</summary>
public class GameServicePresencialTests
{
    private const long Owner = 100L;
    private readonly InMemoryGameSessionStore _store = new();
    private readonly List<(string Method, object? Payload)> _sent = new();
    private readonly List<(string ConnectionId, string Method, object? Payload)> _sentToClient = new();
    private readonly GameService _service;
    private List<Pregunta> _questions = FourAnswerQuestions(3);

    public GameServicePresencialTests()
    {
        var groupProxy = new Mock<IClientProxy>();
        groupProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => _sent.Add((method, args.FirstOrDefault())))
            .Returns(Task.CompletedTask);

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(groupProxy.Object);
        hubClients.Setup(c => c.Client(It.IsAny<string>())).Returns((string connectionId) =>
        {
            var clientProxy = new Mock<ISingleClientProxy>();
            clientProxy
                .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?[], CancellationToken>((method, args, _) =>
                    _sentToClient.Add((connectionId, method, args.FirstOrDefault())))
                .Returns(Task.CompletedTask);
            return clientProxy.Object;
        });
        var hubContext = new Mock<IHubContext<GameHub>>();
        hubContext.Setup(h => h.Clients).Returns(hubClients.Object);

        var quizRepository = new Mock<IQuizRepository>();
        quizRepository.Setup(r => r.FindByIdAsync(It.IsAny<long>()))
            .ReturnsAsync((long id) => new Quiz { Id = id, Nombre = "Test Quiz" });
        quizRepository.Setup(r => r.GetQuestionsWithAnswersAsync(It.IsAny<long>()))
            .ReturnsAsync(() => _questions);

        var provider = new Mock<IServiceProvider>();
        provider.Setup(sp => sp.GetService(typeof(IHubContext<GameHub>))).Returns(hubContext.Object);
        provider.Setup(sp => sp.GetService(typeof(IQuizRepository))).Returns(quizRepository.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var options = new GameOptions
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
        _service = new GameService(_store, options, new Mock<ILogger<GameService>>().Object, scopeFactory.Object);
    }

    private static List<Pregunta> FourAnswerQuestions(int count, int fase = 1, int firstId = 1) =>
        Enumerable.Range(firstId, count).Select(i => new Pregunta
        {
            Id = i,
            NumeroPregunta = i,
            FaseNumero = fase,
            Enunciado = $"Pregunta {i}",
            Respuestas =
            [
                new Respuesta { Id = i * 10 + 1, Texto = "Correcta", EsCorrecta = true },
                new Respuesta { Id = i * 10 + 2, Texto = "Mal 1", EsCorrecta = false },
                new Respuesta { Id = i * 10 + 3, Texto = "Mal 2", EsCorrecta = false },
                new Respuesta { Id = i * 10 + 4, Texto = "Mal 3", EsCorrecta = false }
            ]
        }).ToList();

    private async Task<string> StartRoomAsync(int players = 3, GameMode mode = GameMode.Presencial, int? turnTime = 20)
    {
        var roomCode = await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner", turnTime, mode);
        for (var i = 0; i < players; i++)
        {
            var id = 200L + i * 100;
            await _service.JoinGameAsync(roomCode, id, $"p{id}", $"conn-{id}");
        }
        Assert.NotNull(await _service.StartGameAsync(roomCode, Owner));
        _sent.Clear();
        _sentToClient.Clear();
        return roomCode;
    }

    private async Task<GameSessionDocument> SessionAsync(string roomCode) => (await _store.GetAsync(roomCode))!;

    private static QuestionSnapshot Current(GameSessionDocument s) => s.Questions[s.CurrentQuestionIndex];

    private static int CorrectIndex(GameSessionDocument s) => Current(s).Respuestas.FindIndex(r => r.EsCorrecta);

    private static int WrongIndex(GameSessionDocument s) =>
        Enumerable.Range(0, Current(s).Respuestas.Count).First(i => !Current(s).Respuestas[i].EsCorrecta && !s.EliminatedAnswerIndexes.Contains(i));

    private static long Bystander(GameSessionDocument s) => s.TurnQueue.First(id => id != s.GetCurrentPlayerId());

    private async Task<TurnResultDto> MarkAndConfirmAsync(string roomCode, int answerIndex)
    {
        var s = await SessionAsync(roomCode);
        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, Current(s).Id, answerIndex)).IsSuccess);
        var result = await _service.ConfirmAnswerAsync(roomCode, Owner, Current(s).Id);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    // ========== Creación y arranque ==========

    [Fact]
    public async Task CreateGame_Presencial_ForcesNoTimeLimit()
    {
        var roomCode = await StartRoomAsync(turnTime: 30);
        var s = await SessionAsync(roomCode);

        Assert.Equal(GameMode.Presencial, s.Mode);
        Assert.Equal(0, s.TurnTimeLimitSeconds);
        Assert.Null(s.TurnDeadlineUnixMs);
    }

    [Fact]
    public async Task StartGame_Presencial_WithOnePlayerBesidesHost()
    {
        var roomCode = await StartRoomAsync(players: 1);
        var s = await SessionAsync(roomCode);

        Assert.Equal(GameState.Playing, s.State);
        Assert.Single(s.TurnQueue);
    }

    [Fact]
    public async Task TurnStarted_CorrectAnswerOnlySentToHostConnection()
    {
        await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner", null, GameMode.Presencial);
        var roomCode = (await _store.GetUserRoomAsync(Owner))!;
        await _service.JoinGameAsync(roomCode, 200L, "p200", "conn-200");
        await _service.StartGameAsync(roomCode, Owner);
        var s = await SessionAsync(roomCode);

        var hostInfo = Assert.Single(_sentToClient);
        Assert.Equal("conn-owner", hostInfo.ConnectionId);
        Assert.Equal("HostQuestionInfo", hostInfo.Method);
        var dto = Assert.IsType<HostQuestionInfoDto>(hostInfo.Payload);
        Assert.Equal(Current(s).Id, dto.QuestionId);
        Assert.Equal(CorrectIndex(s), dto.CorrectAnswerIndex);

        var turn = Assert.IsType<TurnStartedDto>(_sent.Single(m => m.Method == "TurnStarted").Payload);
        Assert.Equal("Presencial", turn.Mode);
        Assert.DoesNotContain(_sent, m => m.Method == "HostQuestionInfo");
    }

    [Fact]
    public async Task NormalMode_NeverSendsHostInfo()
    {
        var roomCode = await StartRoomAsync(mode: GameMode.Normal);
        var s = await SessionAsync(roomCode);

        await _service.SubmitAnswerAsync(roomCode, s.GetCurrentPlayerId()!.Value, Current(s).Id, CorrectIndex(s));

        Assert.Empty(_sentToClient);
    }

    // ========== Marcar ==========

    [Fact]
    public async Task MarkAnswer_DoesNotScore_BroadcastsAndCanBeChangedOrCleared()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;
        var questionId = Current(s).Id;

        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, questionId, WrongIndex(s))).IsSuccess);
        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, questionId, CorrectIndex(s))).IsSuccess);

        s = await SessionAsync(roomCode);
        Assert.Equal(CorrectIndex(s), s.MarkedAnswerIndex);
        Assert.Equal(0, s.Players.Single(p => p.UserId == player).Score);
        Assert.Equal(questionId, Current(s).Id);
        var marks = _sent.Where(m => m.Method == "AnswerMarked").Select(m => (AnswerMarkedDto)m.Payload!).ToList();
        Assert.Equal([WrongIndex(s), CorrectIndex(s)], marks.Select(m => m.AnswerIndex!.Value));
        Assert.DoesNotContain(_sent, m => m.Method == "TurnResult");

        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, questionId, null)).IsSuccess);
        Assert.Null((await SessionAsync(roomCode)).MarkedAnswerIndex);
        Assert.Null(((AnswerMarkedDto)_sent.Last(m => m.Method == "AnswerMarked").Payload!).AnswerIndex);
    }

    [Fact]
    public async Task MarkAnswer_RejectsNonHost_NormalMode_StaleQuestionAndInvalidIndex()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;

        Assert.True((await _service.MarkAnswerAsync(roomCode, player, Current(s).Id, 0)).IsFailure);
        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, Current(s).Id + 999, 0)).IsFailure);
        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, Current(s).Id, 7)).IsFailure);

        var normalRoom = await StartRoomAsync(mode: GameMode.Normal);
        var normal = await SessionAsync(normalRoom);
        Assert.True((await _service.MarkAnswerAsync(normalRoom, Owner, Current(normal).Id, 0)).IsFailure);
    }

    [Fact]
    public async Task SubmitAnswer_IsRejectedInPresencialMode()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        Assert.Null(await _service.SubmitAnswerAsync(roomCode, s.GetCurrentPlayerId()!.Value, Current(s).Id, CorrectIndex(s)));
    }

    // ========== Confirmar y pasar de pregunta ==========

    [Fact]
    public async Task ConfirmAnswer_WithoutMark_Fails()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        Assert.True((await _service.ConfirmAnswerAsync(roomCode, Owner, Current(s).Id)).IsFailure);
    }

    [Fact]
    public async Task ConfirmAnswer_ScoresTurnPlayer_AndWaitsForHost()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;
        var questionIndex = s.CurrentQuestionIndex;

        var result = await MarkAndConfirmAsync(roomCode, CorrectIndex(s));

        Assert.True(result.IsCorrect);
        Assert.Equal(player, result.PlayerId);
        Assert.Equal(100, result.PointsEarned);
        s = await SessionAsync(roomCode);
        Assert.Equal(100, s.Players.Single(p => p.UserId == player).Score);
        Assert.True(s.AwaitingNextQuestion);
        Assert.Null(s.MarkedAnswerIndex);
        Assert.Equal(questionIndex, s.CurrentQuestionIndex);
        Assert.Equal(player, s.GetCurrentPlayerId());
        Assert.Contains(_sent, m => m.Method == "TurnResult");
        Assert.DoesNotContain(_sent, m => m.Method == "TurnStarted");

        // Ya confirmada: ni se puede volver a marcar/confirmar ni usar comodines
        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, Current(s).Id, 0)).IsFailure);
        Assert.True((await _service.ConfirmAnswerAsync(roomCode, Owner, Current(s).Id)).IsFailure);
        Assert.True((await _service.UseComodinAsync(roomCode, Bystander(s), ComodinTipo.Robo, Current(s).Id)).IsFailure);
    }

    [Fact]
    public async Task ConfirmAnswer_Wrong_ScoresZero()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        var result = await MarkAndConfirmAsync(roomCode, WrongIndex(s));

        Assert.False(result.IsCorrect);
        Assert.Equal(0, result.PointsEarned);
        Assert.Equal(CorrectIndex(s), result.CorrectAnswerIndex);
    }

    [Fact]
    public async Task NextQuestion_RequiresConfirmation_ThenAdvancesTurn()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var first = s.GetCurrentPlayerId();

        Assert.True((await _service.NextQuestionAsync(roomCode, Owner)).IsFailure);

        await MarkAndConfirmAsync(roomCode, CorrectIndex(s));
        Assert.True((await _service.NextQuestionAsync(roomCode, s.TurnQueue[1])).IsFailure);
        _sent.Clear();
        _sentToClient.Clear();

        Assert.True((await _service.NextQuestionAsync(roomCode, Owner)).IsSuccess);

        s = await SessionAsync(roomCode);
        Assert.Equal(1, s.CurrentQuestionIndex);
        Assert.NotEqual(first, s.GetCurrentPlayerId());
        Assert.False(s.AwaitingNextQuestion);
        Assert.Contains(_sent, m => m.Method == "TurnStarted");
        Assert.Equal("HostQuestionInfo", Assert.Single(_sentToClient).Method);
    }

    [Fact]
    public async Task NextQuestion_OnLastQuestion_FinishesGame()
    {
        _questions = FourAnswerQuestions(1);
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        await MarkAndConfirmAsync(roomCode, CorrectIndex(s));
        Assert.Equal(GameState.Playing, (await SessionAsync(roomCode)).State);

        Assert.True((await _service.NextQuestionAsync(roomCode, Owner)).IsSuccess);
        Assert.Equal(GameState.Finished, (await SessionAsync(roomCode)).State);
        Assert.Contains(_sent, m => m.Method == "GameFinished");
    }

    [Fact]
    public async Task NextQuestion_AtPhaseChange_EntersPhaseBreak()
    {
        _questions = [.. FourAnswerQuestions(1, fase: 1), .. FourAnswerQuestions(1, fase: 2, firstId: 2)];
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        await MarkAndConfirmAsync(roomCode, CorrectIndex(s));
        Assert.True((await _service.NextQuestionAsync(roomCode, Owner)).IsSuccess);

        Assert.Equal(GameState.PhaseBreak, (await SessionAsync(roomCode)).State);
    }

    // ========== Comodines ==========

    [Fact]
    public async Task DobleONada_And_Bets_SettleOnHostConfirmation()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;
        var bettor = Bystander(s);

        Assert.True((await _service.UseComodinAsync(roomCode, player, ComodinTipo.DobleONada, Current(s).Id)).IsSuccess);
        Assert.True((await _service.UseComodinAsync(roomCode, bettor, ComodinTipo.Apuesta, Current(s).Id, true)).IsSuccess);

        var result = await MarkAndConfirmAsync(roomCode, CorrectIndex(s));

        Assert.Equal(200, result.PointsEarned);
        var bet = Assert.Single(result.Bets!);
        Assert.True(bet.Won);
        Assert.Equal(50, (await SessionAsync(roomCode)).Players.Single(p => p.UserId == bettor).Score);
    }

    [Fact]
    public async Task Comodines_CanBeUsedWhileAnswerIsMarked()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, Current(s).Id, WrongIndex(s))).IsSuccess);

        Assert.True((await _service.UseComodinAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.DobleONada, Current(s).Id)).IsSuccess);
        Assert.Equal(WrongIndex(s), (await SessionAsync(roomCode)).MarkedAnswerIndex);
    }

    [Fact]
    public async Task Robo_ClearsMark_AndHostAnswersForThief()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var thief = Bystander(s);
        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, Current(s).Id, WrongIndex(s))).IsSuccess);

        Assert.True((await _service.UseComodinAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id)).IsSuccess);

        s = await SessionAsync(roomCode);
        Assert.Null(s.MarkedAnswerIndex);
        var turn = Assert.IsType<TurnStartedDto>(_sent.Last(m => m.Method == "TurnStarted").Payload);
        Assert.Null(turn.MarkedAnswerIndex);

        var result = await MarkAndConfirmAsync(roomCode, CorrectIndex(s));
        Assert.Equal(thief, result.PlayerId);
        Assert.True(result.IsSteal);
    }

    [Fact]
    public async Task FailedRobo_ReturnsToOriginal_WhoCanThenBeConfirmed()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var original = s.GetCurrentPlayerId()!.Value;
        var thief = Bystander(s);
        await _service.UseComodinAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id);

        var failed = await MarkAndConfirmAsync(roomCode, WrongIndex(s));

        Assert.Equal(original, failed.ReturnsToPlayerId);
        s = await SessionAsync(roomCode);
        Assert.False(s.AwaitingNextQuestion);
        Assert.Equal(original, s.GetAnsweringPlayerId());

        var result = await MarkAndConfirmAsync(roomCode, CorrectIndex(s));
        Assert.Equal(original, result.PlayerId);
        Assert.True((await SessionAsync(roomCode)).AwaitingNextQuestion);
    }

    [Fact]
    public async Task Ruleta_EliminatingMarkedAnswer_ClearsMark()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var roomCode = await StartRoomAsync();
            var s = await SessionAsync(roomCode);
            // Marcar todas las incorrectas una a una no es posible; se marca una y se reintenta hasta que la ruleta la elimine.
            var marked = WrongIndex(s);
            await _service.MarkAnswerAsync(roomCode, Owner, Current(s).Id, marked);
            _sent.Clear();

            var ruleta = await _service.UseComodinAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.Ruleta, Current(s).Id);
            Assert.True(ruleta.IsSuccess);

            s = await SessionAsync(roomCode);
            if (ruleta.Value.EliminatedAnswerIndexes!.Contains(marked))
            {
                Assert.Null(s.MarkedAnswerIndex);
                Assert.Null(((AnswerMarkedDto)_sent.Single(m => m.Method == "AnswerMarked").Payload!).AnswerIndex);
                return;
            }

            Assert.Equal(marked, s.MarkedAnswerIndex);
            Assert.DoesNotContain(_sent, m => m.Method == "AnswerMarked");
        }
        Assert.Fail("La ruleta nunca eliminó la respuesta marcada en 50 intentos.");
    }

    // ========== Reconexión ==========

    [Fact]
    public async Task Rejoin_IncludesMarkHostInfoAndPendingResult()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        await _service.MarkAnswerAsync(roomCode, Owner, Current(s).Id, WrongIndex(s));

        var rejoin = (await _service.GetRejoinStateAsync(roomCode))!;
        Assert.Equal("Presencial", rejoin.GameState.Mode);
        Assert.Equal(WrongIndex(s), rejoin.Turn!.MarkedAnswerIndex);
        Assert.Equal(CorrectIndex(s), rejoin.HostInfo!.CorrectAnswerIndex);
        Assert.Null(rejoin.LastTurnResult);

        await _service.ConfirmAnswerAsync(roomCode, Owner, Current(s).Id);

        rejoin = (await _service.GetRejoinStateAsync(roomCode))!;
        Assert.NotNull(rejoin.Turn);
        Assert.False(rejoin.LastTurnResult!.IsCorrect);
    }
}
