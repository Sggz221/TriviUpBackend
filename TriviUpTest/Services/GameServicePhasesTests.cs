using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Game.Configuration;
using TriviUpBackend.Game.Hubs;
using TriviUpBackend.Game.Models;
using TriviUpBackend.Game.Persistence;
using TriviUpBackend.Game.Services;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;

namespace TriviUpTest.Services;

/// <summary>Fases de una partida: orden, intermedio entre fases, continuar y reconexión.</summary>
public class GameServicePhasesTests
{
    private readonly InMemoryGameSessionStore _store = new();
    private readonly Mock<IClientProxy> _groupProxy = new();
    private readonly GameService _service;
    private List<Pregunta> _questions;

    public GameServicePhasesTests()
    {
        _questions = TwoPhaseQuestions();

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_groupProxy.Object);
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
            RoomLockTimeoutMs = 2000,
            DeadlinePollIntervalMs = 50
        };
        _service = new GameService(_store, options, new Mock<ILogger<GameService>>().Object, scopeFactory.Object);
    }

    private static Pregunta Q(long id, int fase, string? nombre, string? color = null) => new()
    {
        Id = id,
        NumeroPregunta = (int)id,
        Enunciado = $"Pregunta {id}",
        FaseNumero = fase,
        FaseNombre = nombre,
        FaseColor = color,
        Respuestas =
        [
            new Respuesta { Id = id * 10 + 1, Texto = "Sí", EsCorrecta = true },
            new Respuesta { Id = id * 10 + 2, Texto = "No", EsCorrecta = false }
        ]
    };

    private static List<Pregunta> TwoPhaseQuestions() =>
    [
        Q(1, 1, "Ronda 1"), Q(2, 1, "Ronda 1"), Q(3, 2, "Ronda 2"), Q(4, 2, "Ronda 2")
    ];

    private async Task<string> StartRoomAsync()
    {
        var roomCode = await _service.CreateGameAsync(1L, 100L, "owner", "conn-owner");
        await _service.JoinGameAsync(roomCode, 200L, "p1", "conn-200");
        await _service.JoinGameAsync(roomCode, 300L, "p2", "conn-300");
        Assert.NotNull(await _service.StartGameAsync(roomCode, 100L));
        return roomCode;
    }

    /// <summary>Responde la pregunta actual con el jugador al que le toca.</summary>
    private async Task AnswerCurrentAsync(string roomCode)
    {
        var session = (await _store.GetAsync(roomCode))!;
        var playerId = session.GetCurrentPlayerId()!.Value;
        var question = session.Questions[session.CurrentQuestionIndex];
        Assert.NotNull(await _service.SubmitAnswerAsync(roomCode, playerId, question.Id, 0));
    }

    private void VerifySent(string method, Times times) =>
        _groupProxy.Verify(p => p.SendCoreAsync(method, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task StartGame_PlaysPhasesInOrder_ShufflingOnlyInsideEachPhase()
    {
        // Preguntas dadas en desorden: el resultado debe seguir agrupado por fase
        _questions = [Q(3, 2, "Ronda 2"), Q(1, 1, "Ronda 1"), Q(4, 2, "Ronda 2"), Q(2, 1, "Ronda 1")];

        for (var i = 0; i < 20; i++)
        {
            var roomCode = await StartRoomAsync();
            var session = (await _store.GetAsync(roomCode))!;

            Assert.Equal([1, 1, 2, 2], session.Questions.Select(q => q.FaseNumero));
            Assert.Equal(["Ronda 1", "Ronda 1", "Ronda 2", "Ronda 2"], session.Questions.Select(q => q.FaseNombre));
        }
    }

    [Fact]
    public async Task Submit_LastQuestionOfPhase_EntersPhaseBreakWithoutStartingNextTurn()
    {
        var roomCode = await StartRoomAsync();

        await AnswerCurrentAsync(roomCode);
        var afterFirst = (await _store.GetAsync(roomCode))!;
        Assert.Equal(GameState.Playing, afterFirst.State);

        await AnswerCurrentAsync(roomCode);
        var session = (await _store.GetAsync(roomCode))!;

        Assert.Equal(GameState.PhaseBreak, session.State);
        Assert.Null(session.TurnDeadlineUnixMs);
        Assert.Equal(2, session.CurrentQuestionIndex);
        VerifySent("PhaseCompleted", Times.Once());
        VerifySent("TurnStarted", Times.Exactly(2)); // inicio + segunda pregunta de la fase 1; no la de la fase 2
    }

    [Fact]
    public async Task ContinuePhase_Owner_ResumesPlayWithNextPhaseQuestion()
    {
        var roomCode = await StartRoomAsync();
        await AnswerCurrentAsync(roomCode);
        await AnswerCurrentAsync(roomCode);
        var generationAtBreak = (await _store.GetAsync(roomCode))!.TurnGeneration;

        var result = await _service.ContinuePhaseAsync(roomCode, 100L);

        Assert.True(result.IsSuccess);
        var session = (await _store.GetAsync(roomCode))!;
        Assert.Equal(GameState.Playing, session.State);
        Assert.True(session.TurnDeadlineUnixMs.HasValue);
        Assert.True(session.TurnGeneration > generationAtBreak);
        Assert.Equal(2, session.Questions[session.CurrentQuestionIndex].FaseNumero);
        VerifySent("TurnStarted", Times.Exactly(3));
    }

    [Fact]
    public async Task ContinuePhase_NonOwner_Fails()
    {
        var roomCode = await StartRoomAsync();
        await AnswerCurrentAsync(roomCode);
        await AnswerCurrentAsync(roomCode);

        var result = await _service.ContinuePhaseAsync(roomCode, 200L);

        Assert.True(result.IsFailure);
        Assert.Equal(GameState.PhaseBreak, (await _store.GetAsync(roomCode))!.State);
    }

    [Fact]
    public async Task ContinuePhase_WhenNotBetweenPhases_Fails()
    {
        var roomCode = await StartRoomAsync();

        Assert.True((await _service.ContinuePhaseAsync(roomCode, 100L)).IsFailure);
    }

    [Fact]
    public async Task SinglePhaseQuiz_NeverEntersPhaseBreak()
    {
        _questions = [Q(1, 1, null), Q(2, 1, null), Q(3, 1, null)];
        var roomCode = await StartRoomAsync();

        await AnswerCurrentAsync(roomCode);
        await AnswerCurrentAsync(roomCode);

        Assert.Equal(GameState.Playing, (await _store.GetAsync(roomCode))!.State);
        VerifySent("PhaseCompleted", Times.Never());
    }

    [Fact]
    public async Task LastPhase_EndsGameInsteadOfPhaseBreak()
    {
        var roomCode = await StartRoomAsync();
        await AnswerCurrentAsync(roomCode);
        await AnswerCurrentAsync(roomCode);
        Assert.True((await _service.ContinuePhaseAsync(roomCode, 100L)).IsSuccess);
        await AnswerCurrentAsync(roomCode);
        await AnswerCurrentAsync(roomCode);

        Assert.Equal(GameState.Finished, (await _store.GetAsync(roomCode))!.State);
        VerifySent("PhaseCompleted", Times.Once());
    }

    [Fact]
    public async Task GetRejoinState_DuringPhaseBreak_ReturnsPhaseSummaryWithoutTurn()
    {
        var roomCode = await StartRoomAsync();
        await AnswerCurrentAsync(roomCode);
        await AnswerCurrentAsync(roomCode);

        var rejoin = await _service.GetRejoinStateAsync(roomCode);

        Assert.NotNull(rejoin);
        Assert.Null(rejoin!.Turn);
        Assert.NotNull(rejoin.PhaseBreak);
        Assert.Equal(1, rejoin.PhaseBreak!.FaseNumero);
        Assert.Equal("Ronda 1", rejoin.PhaseBreak.FaseNombre);
        Assert.Equal("Ronda 2", rejoin.PhaseBreak.SiguienteFaseNombre);
        Assert.Equal(2, rejoin.PhaseBreak.TotalFases);
    }

    [Fact]
    public async Task PhaseColors_TravelInTurnAndPhaseBreakDtos()
    {
        _questions = [Q(1, 1, "Ronda 1", "#ff0000"), Q(2, 2, "Ronda 2", "#00ff00")];
        var roomCode = await StartRoomAsync();

        var jugando = await _service.GetRejoinStateAsync(roomCode);
        Assert.Equal("#ff0000", jugando!.Turn!.FaseColor);

        await AnswerCurrentAsync(roomCode);
        var intermedio = (await _service.GetRejoinStateAsync(roomCode))!.PhaseBreak!;

        Assert.Equal("#ff0000", intermedio.FaseColor);
        Assert.Equal(2, intermedio.SiguienteFaseNumero);
        Assert.Equal("#00ff00", intermedio.SiguienteFaseColor);
    }

    [Fact]
    public async Task GetRejoinState_Playing_IncludesPhaseInfo()
    {
        var roomCode = await StartRoomAsync();

        var rejoin = await _service.GetRejoinStateAsync(roomCode);

        Assert.Null(rejoin!.PhaseBreak);
        Assert.Equal(1, rejoin.Turn!.FaseNumero);
        Assert.Equal("Ronda 1", rejoin.Turn.FaseNombre);
        Assert.Equal(2, rejoin.Turn.TotalFases);
    }
}
