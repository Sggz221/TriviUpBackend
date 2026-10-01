using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Game.Configuration;
using TriviUpBackend.Game.Hubs;
using TriviUpBackend.Game.Models;
using TriviUpBackend.Game.Persistence;
using TriviUpBackend.Game.Services;
using TriviUpBackend.Cuestionarios.Entities;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;

namespace TriviUpTest.Services;

/// <summary>Ronda dinámica: el primer equipo en pulsar desde su móvil se lleva la pregunta.</summary>
public class GameServiceDynamicRoundTests
{
    private readonly InMemoryGameSessionStore _store = new();
    private readonly Mock<IClientProxy> _groupProxy = new();
    private readonly GameService _service;
    private List<Pregunta> _questions;

    public GameServiceDynamicRoundTests()
    {
        _questions =
        [
            Q(1, 1, dinamica: true), Q(2, 1, dinamica: true), Q(3, 2, dinamica: false)
        ];

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

    private static Pregunta Q(long id, int fase, bool dinamica) => new()
    {
        Id = id,
        NumeroPregunta = (int)id,
        Enunciado = $"Pregunta {id}",
        FaseNumero = fase,
        Tipo = dinamica ? TiposPregunta.Pulsador : TiposPregunta.Normal,
        Respuestas =
        [
            new Respuesta { Id = id * 10 + 1, Texto = "Sí", EsCorrecta = true },
            new Respuesta { Id = id * 10 + 2, Texto = "No", EsCorrecta = false }
        ]
    };

    private async Task<string> StartRoomAsync()
    {
        var roomCode = await _service.CreateGameAsync(1L, 100L, "owner", "conn-owner");
        await _service.JoinGameAsync(roomCode, 200L, "p1", "conn-200");
        await _service.JoinGameAsync(roomCode, 300L, "p2", "conn-300");
        Assert.NotNull(await _service.StartGameAsync(roomCode, 100L));
        return roomCode;
    }

    private void VerifySent(string method, Times times) =>
        _groupProxy.Verify(p => p.SendCoreAsync(method, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task StartGame_DynamicQuestion_OpensBuzzerWithNobodyAnswering()
    {
        var roomCode = await StartRoomAsync();
        var session = (await _store.GetAsync(roomCode))!;

        Assert.True(session.BuzzerOpen);
        Assert.Null(session.GetAnsweringPlayerId());
        Assert.Null(session.BuzzWinnerId);
    }

    [Fact]
    public async Task Buzz_FirstPlayerWins_AndSecondIsRejected()
    {
        var roomCode = await StartRoomAsync();
        var questionId = (await _store.GetAsync(roomCode))!.Questions[0].Id;

        var first = await _service.BuzzAsync(roomCode, 300L, questionId);
        var second = await _service.BuzzAsync(roomCode, 200L, questionId);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        var session = (await _store.GetAsync(roomCode))!;
        Assert.False(session.BuzzerOpen);
        Assert.Equal(300L, session.BuzzWinnerId);
        Assert.Equal(300L, session.GetAnsweringPlayerId());
        VerifySent("BuzzerWon", Times.Once());
    }

    [Fact]
    public async Task Buzz_ConcurrentPlayers_ExactlyOneWins()
    {
        var roomCode = await StartRoomAsync();
        var questionId = (await _store.GetAsync(roomCode))!.Questions[0].Id;

        var results = await Task.WhenAll(
            _service.BuzzAsync(roomCode, 200L, questionId),
            _service.BuzzAsync(roomCode, 300L, questionId));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
    }

    [Fact]
    public async Task Buzz_OwnerOrStaleQuestion_IsRejected()
    {
        var roomCode = await StartRoomAsync();
        var questionId = (await _store.GetAsync(roomCode))!.Questions[0].Id;

        Assert.True((await _service.BuzzAsync(roomCode, 100L, questionId)).IsFailure);
        Assert.True((await _service.BuzzAsync(roomCode, 200L, questionId + 999)).IsFailure);
        Assert.True((await _store.GetAsync(roomCode))!.BuzzerOpen);
    }

    [Fact]
    public async Task Submit_BeforeAnyoneBuzzes_IsRejected()
    {
        var roomCode = await StartRoomAsync();
        var questionId = (await _store.GetAsync(roomCode))!.Questions[0].Id;

        Assert.Null(await _service.SubmitAnswerAsync(roomCode, 200L, questionId, 0));
        Assert.Null(await _service.SubmitAnswerAsync(roomCode, 300L, questionId, 0));
    }

    [Fact]
    public async Task Submit_OnlyTheBuzzWinnerCanAnswer_AndGameMovesOn()
    {
        var roomCode = await StartRoomAsync();
        var questionId = (await _store.GetAsync(roomCode))!.Questions[0].Id;
        await _service.BuzzAsync(roomCode, 300L, questionId);

        Assert.Null(await _service.SubmitAnswerAsync(roomCode, 200L, questionId, 0));
        Assert.NotNull(await _service.SubmitAnswerAsync(roomCode, 300L, questionId, 0));

        var session = (await _store.GetAsync(roomCode))!;
        Assert.Equal(1, session.CurrentQuestionIndex);
        // La siguiente pregunta también es dinámica: el pulsador vuelve a abrirse.
        Assert.True(session.BuzzerOpen);
        Assert.Null(session.BuzzWinnerId);
    }

    [Fact]
    public async Task Comodin_InDynamicQuestion_IsRejected()
    {
        var roomCode = await StartRoomAsync();
        var questionId = (await _store.GetAsync(roomCode))!.Questions[0].Id;
        await _service.BuzzAsync(roomCode, 200L, questionId);

        var result = await _service.UseComodinAsync(roomCode, 200L, ComodinTipo.Ruleta, questionId);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ProcessDueTimeout_WithBuzzerOpen_SkipsQuestionWithoutPoints()
    {
        var roomCode = await StartRoomAsync();
        var session = (await _store.GetAsync(roomCode))!;
        var generation = session.TurnGeneration;
        session.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
        await _store.SaveAsync(session);

        await _service.ProcessDueTimeoutAsync(roomCode, generation);

        var after = (await _store.GetAsync(roomCode))!;
        Assert.Equal(1, after.CurrentQuestionIndex);
        Assert.All(after.Players, p => Assert.Equal(0, p.Score));
        Assert.All(after.Players, p => Assert.Equal(0, p.WrongAnswers));
    }

    [Fact]
    public async Task NextQuestion_Owner_CanSkipQuestionNobodyBuzzed()
    {
        var roomCode = await StartRoomAsync();

        var result = await _service.NextQuestionAsync(roomCode, 100L);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, (await _store.GetAsync(roomCode))!.CurrentQuestionIndex);
    }

    [Fact]
    public async Task Rejoin_WithBuzzerOpen_ReportsDynamicQuestionWithNoAnsweringPlayer()
    {
        var roomCode = await StartRoomAsync();

        var rejoin = await _service.GetRejoinStateAsync(roomCode);

        Assert.NotNull(rejoin?.Turn);
        Assert.True(rejoin!.Turn!.IsDynamic);
        Assert.True(rejoin.Turn.BuzzerOpen);
        Assert.Equal(0, rejoin.Turn.CurrentPlayerId);
    }

    [Fact]
    public async Task NonDynamicQuestion_DoesNotOpenBuzzer()
    {
        _questions = [Q(1, 1, dinamica: false), Q(2, 1, dinamica: false)];
        var roomCode = await StartRoomAsync();
        var session = (await _store.GetAsync(roomCode))!;
        var questionId = session.Questions[0].Id;

        Assert.False(session.BuzzerOpen);
        Assert.NotNull(session.GetAnsweringPlayerId());
        Assert.True((await _service.BuzzAsync(roomCode, 200L, questionId)).IsFailure);
    }
}
