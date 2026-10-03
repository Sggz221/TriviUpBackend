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

/// <summary>Comodines: Ruleta, Doble o nada, Robo y Apuesta; y bonus de tiempo calculado en el servidor.</summary>
public class GameServiceComodinesTests
{
    private const long Owner = 100L;
    private readonly InMemoryGameSessionStore _store = new();
    private readonly Mock<IClientProxy> _groupProxy = new();
    private readonly List<(string Method, object? Payload)> _sent = new();
    private readonly GameService _service;
    private List<Pregunta> _questions = FourAnswerQuestions(3);

    public GameServiceComodinesTests()
    {
        _groupProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => _sent.Add((method, args.FirstOrDefault())))
            .Returns(Task.CompletedTask);

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_groupProxy.Object);
        hubClients.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());
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

    private static List<Pregunta> FourAnswerQuestions(int count) => Enumerable.Range(1, count).Select(i => new Pregunta
    {
        Id = i,
        NumeroPregunta = i,
        Enunciado = $"Pregunta {i}",
        Respuestas =
        [
            new Respuesta { Id = i * 10 + 1, Texto = "Correcta", EsCorrecta = true },
            new Respuesta { Id = i * 10 + 2, Texto = "Mal 1", EsCorrecta = false },
            new Respuesta { Id = i * 10 + 3, Texto = "Mal 2", EsCorrecta = false },
            new Respuesta { Id = i * 10 + 4, Texto = "Mal 3", EsCorrecta = false }
        ]
    }).ToList();

    private async Task<string> StartRoomAsync(int players = 3)
    {
        var roomCode = await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner");
        for (var i = 0; i < players; i++)
        {
            var id = 200L + i * 100;
            await _service.JoinGameAsync(roomCode, id, $"p{id}", $"conn-{id}");
        }
        Assert.NotNull(await _service.StartGameAsync(roomCode, Owner));
        _sent.Clear();
        return roomCode;
    }

    private async Task<GameSessionDocument> SessionAsync(string roomCode) => (await _store.GetAsync(roomCode))!;

    private static QuestionSnapshot Current(GameSessionDocument s) => s.Questions[s.CurrentQuestionIndex];

    private static int CorrectIndex(GameSessionDocument s) => Current(s).Respuestas.FindIndex(r => r.EsCorrecta);

    private static int WrongIndex(GameSessionDocument s) =>
        Enumerable.Range(0, Current(s).Respuestas.Count).First(i => !Current(s).Respuestas[i].EsCorrecta && !s.EliminatedAnswerIndexes.Contains(i));

    /// <summary>Un jugador que no tiene el turno.</summary>
    private static long Bystander(GameSessionDocument s, params long[] except) =>
        s.TurnQueue.First(id => id != s.GetCurrentPlayerId() && !except.Contains(id));

    private async Task SetScoreAsync(string roomCode, long userId, int score)
    {
        var s = await SessionAsync(roomCode);
        s.Players.Single(p => p.UserId == userId).Score = score;
        await _store.SaveAsync(s);
    }

    private Task<CSharpFunctionalExtensions.Result<ComodinUsedDto>> UseAsync(
        string roomCode, long userId, ComodinTipo tipo, long questionId, bool? prediccion = null) =>
        _service.UseComodinAsync(roomCode, userId, tipo, questionId, prediccion);

    // ========== Ruleta ==========

    [Fact]
    public void HuecosRuleta_OneAndTwoEquallyLikely_ExtremesLessLikely()
    {
        var huecos = ComodinReglas.HuecosRuleta;
        int Count(int v) => huecos.Count(h => h == v);

        Assert.Equal(20, huecos.Count);
        Assert.Equal(Count(1), Count(2));
        Assert.True(Count(1) > Count(0));
        Assert.True(Count(1) > Count(3));
        // Mezclados: nunca dos huecos iguales seguidos (tampoco al dar la vuelta)
        for (var i = 0; i < huecos.Count; i++)
        {
            Assert.NotEqual(huecos[i], huecos[(i + 1) % huecos.Count]);
        }
    }

    [Fact]
    public void TirarRuleta_IsUniformOverHuecos_AndReturnsTheirValue()
    {
        var random = new Random(1234);
        var counts = new int[4];
        const int n = 100_000;
        for (var i = 0; i < n; i++)
        {
            var (hueco, valor) = ComodinReglas.TirarRuleta(random);
            Assert.Equal(ComodinReglas.HuecosRuleta[hueco], valor);
            counts[valor]++;
        }

        for (var v = 0; v < 4; v++)
        {
            var expected = ComodinReglas.HuecosRuleta.Count(h => h == v) / (double)ComodinReglas.HuecosRuleta.Count;
            Assert.InRange(counts[v] / (double)n, expected - 0.01, expected + 0.01);
        }
    }

    [Fact]
    public async Task Ruleta_ReportsHuecoAndExtendsTurnDeadlineBySpinDuration()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var deadlineBefore = s.TurnDeadlineUnixMs!.Value;

        var result = await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.Ruleta, Current(s).Id);

        Assert.InRange(result.Value.RuletaHueco!.Value, 0, ComodinReglas.HuecosRuleta.Count - 1);
        Assert.Equal(ComodinReglas.DuracionRuletaMs, result.Value.RuletaDuracionMs);
        Assert.Equal(deadlineBefore + ComodinReglas.DuracionRuletaMs, (await SessionAsync(roomCode)).TurnDeadlineUnixMs);
    }

    [Fact]
    public async Task Ruleta_OnlyEliminatesIncorrectAnswers_AndEliminatedCannotBeChosen()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var roomCode = await StartRoomAsync();
            var s = await SessionAsync(roomCode);
            var player = s.GetCurrentPlayerId()!.Value;

            var result = await UseAsync(roomCode, player, ComodinTipo.Ruleta, Current(s).Id);

            Assert.True(result.IsSuccess);
            var eliminated = result.Value.EliminatedAnswerIndexes!;
            Assert.InRange(eliminated.Count, 0, 3);
            Assert.Equal(eliminated.Count, result.Value.RuletaResultado);
            Assert.All(eliminated, i => Assert.False(Current(s).Respuestas[i].EsCorrecta));

            s = await SessionAsync(roomCode);
            Assert.Equal(eliminated, s.EliminatedAnswerIndexes);
            Assert.DoesNotContain("Ruleta", result.Value.AvailableComodines);

            if (eliminated.Count > 0)
            {
                Assert.Null(await _service.SubmitAnswerAsync(roomCode, player, Current(s).Id, eliminated[0]));
                return;
            }
        }
        Assert.Fail("La ruleta nunca eliminó ninguna respuesta en 50 intentos.");
    }

    [Fact]
    public async Task Ruleta_ClampsToAvailableIncorrectAnswers()
    {
        _questions = [new Pregunta
        {
            Id = 1, NumeroPregunta = 1, Enunciado = "V/F",
            Respuestas = [new Respuesta { Id = 1, Texto = "V", EsCorrecta = true }, new Respuesta { Id = 2, Texto = "F", EsCorrecta = false }]
        }];

        for (var attempt = 0; attempt < 30; attempt++)
        {
            var roomCode = await StartRoomAsync();
            var s = await SessionAsync(roomCode);
            var result = await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.Ruleta, 1);

            Assert.True(result.IsSuccess);
            Assert.InRange(result.Value.EliminatedAnswerIndexes!.Count, 0, 1);
        }
    }

    [Fact]
    public async Task TurnComodines_OutsideOwnTurn_Fail()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var other = Bystander(s);

        Assert.True((await UseAsync(roomCode, other, ComodinTipo.Ruleta, Current(s).Id)).IsFailure);
        Assert.True((await UseAsync(roomCode, other, ComodinTipo.DobleONada, Current(s).Id)).IsFailure);
    }

    [Fact]
    public async Task Comodin_CannotBeUsedTwice()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;

        Assert.True((await UseAsync(roomCode, player, ComodinTipo.Ruleta, Current(s).Id)).IsSuccess);
        Assert.True((await UseAsync(roomCode, player, ComodinTipo.Ruleta, Current(s).Id)).IsFailure);
    }

    // ========== Doble o nada ==========

    [Fact]
    public async Task DobleONada_Correct_EarnsTwiceTheBaseValueWithoutTimeBonus()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;

        Assert.True((await UseAsync(roomCode, player, ComodinTipo.DobleONada, Current(s).Id)).IsSuccess);
        var result = await _service.SubmitAnswerAsync(roomCode, player, Current(s).Id, CorrectIndex(s));

        Assert.True(result!.IsCorrect);
        Assert.True(result.DoubleOrNothing);
        Assert.Equal(200, result.PointsEarned);
        Assert.Equal(200, result.NewTotalScore);
    }

    [Fact]
    public async Task DobleONada_Wrong_LosesOneQuestionValue()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;
        await SetScoreAsync(roomCode, player, 250);

        await UseAsync(roomCode, player, ComodinTipo.DobleONada, Current(s).Id);
        var result = await _service.SubmitAnswerAsync(roomCode, player, Current(s).Id, WrongIndex(s));

        Assert.Equal(-100, result!.PointsEarned);
        Assert.Equal(150, result.NewTotalScore);
    }

    [Fact]
    public async Task DobleONada_Wrong_ScoreNeverGoesBelowZero()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;
        await SetScoreAsync(roomCode, player, 30);

        await UseAsync(roomCode, player, ComodinTipo.DobleONada, Current(s).Id);
        var result = await _service.SubmitAnswerAsync(roomCode, player, Current(s).Id, WrongIndex(s));

        Assert.Equal(-30, result!.PointsEarned);
        Assert.Equal(0, result.NewTotalScore);
    }

    [Fact]
    public async Task DobleONada_Timeout_CountsAsWrong()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;
        await SetScoreAsync(roomCode, player, 100);
        await UseAsync(roomCode, player, ComodinTipo.DobleONada, Current(s).Id);

        s = await SessionAsync(roomCode);
        s.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
        await _store.SaveAsync(s);
        await _service.ProcessDueTimeoutAsync(roomCode, s.TurnGeneration);

        Assert.Equal(0, (await SessionAsync(roomCode)).Players.Single(p => p.UserId == player).Score);
    }

    // ========== Robo ==========

    [Fact]
    public async Task Robo_ThiefAnswersCorrectly_GetsPointsAndQueueContinuesFromOriginal()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var original = s.GetCurrentPlayerId()!.Value;
        var expectedNext = s.TurnQueue[1];
        var thief = s.TurnQueue[2];

        var used = await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id);
        Assert.True(used.IsSuccess);
        Assert.Equal(original, used.Value.StolenFromPlayerId);

        s = await SessionAsync(roomCode);
        Assert.Equal(thief, s.GetAnsweringPlayerId());
        Assert.Equal(original, s.GetCurrentPlayerId());
        Assert.Null(await _service.SubmitAnswerAsync(roomCode, original, Current(s).Id, CorrectIndex(s)));

        var result = await _service.SubmitAnswerAsync(roomCode, thief, Current(s).Id, CorrectIndex(s));

        Assert.True(result!.IsCorrect);
        Assert.True(result.IsSteal);
        Assert.True(result.PointsEarned >= 100);
        var after = await SessionAsync(roomCode);
        Assert.Equal(1, after.CurrentQuestionIndex);
        Assert.Equal(expectedNext, after.GetCurrentPlayerId());
        Assert.Null(after.StolenById);
        Assert.Equal(0, after.Players.Single(p => p.UserId == original).WrongAnswers);
    }

    [Fact]
    public async Task Robo_ThiefFails_LosesHalfQuestionAndTurnReturnsToOriginalWithFullTime()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var original = s.GetCurrentPlayerId()!.Value;
        var thief = Bystander(s);
        await SetScoreAsync(roomCode, thief, 120);

        await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id);
        s = await SessionAsync(roomCode);
        var generationDuringSteal = s.TurnGeneration;
        _sent.Clear();

        var result = await _service.SubmitAnswerAsync(roomCode, thief, Current(s).Id, WrongIndex(s));

        Assert.False(result!.IsCorrect);
        Assert.True(result.IsSteal);
        Assert.Equal(-50, result.PointsEarned);
        Assert.Equal(70, result.NewTotalScore);
        Assert.Equal(-1, result.CorrectAnswerIndex); // no se revela: el original aún tiene que responder
        Assert.Equal(original, result.ReturnsToPlayerId);

        var after = await SessionAsync(roomCode);
        Assert.Equal(0, after.CurrentQuestionIndex);
        Assert.Equal(original, after.GetAnsweringPlayerId());
        Assert.False(after.StealActive);
        Assert.Equal(thief, after.StolenById);
        Assert.True(after.TurnGeneration > generationDuringSteal);
        var remainingMs = after.TurnDeadlineUnixMs!.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.InRange(remainingMs, 20_000, 21_000);

        var turn = (TurnStartedDto)_sent.Single(e => e.Method == "TurnStarted").Payload!;
        Assert.Equal(original, turn.CurrentPlayerId);
        Assert.False(turn.IsSteal);

        // El original responde y ahora sí avanza
        Assert.NotNull(await _service.SubmitAnswerAsync(roomCode, original, Current(after).Id, CorrectIndex(after)));
        Assert.Equal(1, (await SessionAsync(roomCode)).CurrentQuestionIndex);
    }

    [Fact]
    public async Task Robo_ThiefTimesOut_CountsAsFailedSteal()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var original = s.GetCurrentPlayerId()!.Value;
        var thief = Bystander(s);
        await SetScoreAsync(roomCode, thief, 100);
        await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id);

        s = await SessionAsync(roomCode);
        s.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
        await _store.SaveAsync(s);
        await _service.ProcessDueTimeoutAsync(roomCode, s.TurnGeneration);

        var after = await SessionAsync(roomCode);
        Assert.Equal(50, after.Players.Single(p => p.UserId == thief).Score);
        Assert.Equal(original, after.GetAnsweringPlayerId());
        Assert.Equal(0, after.CurrentQuestionIndex);
    }

    [Fact]
    public async Task Robo_OriginalDeadlineBecomesStale()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var originalGeneration = s.TurnGeneration;
        var thief = Bystander(s);
        await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id);

        await _service.ProcessDueTimeoutAsync(roomCode, originalGeneration);

        var after = await SessionAsync(roomCode);
        Assert.True(after.StealActive);
        Assert.Equal(thief, after.GetAnsweringPlayerId());
    }

    [Fact]
    public async Task Robo_InvalidCases_Fail()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var original = s.GetCurrentPlayerId()!.Value;
        var thief = s.TurnQueue[1];
        var another = s.TurnQueue[2];

        Assert.True((await UseAsync(roomCode, original, ComodinTipo.Robo, Current(s).Id)).IsFailure);
        Assert.True((await UseAsync(roomCode, Owner, ComodinTipo.Robo, Current(s).Id)).IsFailure);
        Assert.True((await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id)).IsSuccess);
        Assert.True((await UseAsync(roomCode, another, ComodinTipo.Robo, Current(s).Id)).IsFailure);
    }

    [Fact]
    public async Task Robo_OnlyOnePerQuestion_EvenAfterFailedSteal()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var thief = s.TurnQueue[1];
        var another = s.TurnQueue[2];
        await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id);
        await _service.SubmitAnswerAsync(roomCode, thief, Current(s).Id, WrongIndex(s));

        Assert.True((await UseAsync(roomCode, another, ComodinTipo.Robo, Current(s).Id)).IsFailure);
    }

    [Fact]
    public async Task Robo_ThiefCanUseTurnComodines_AndRuletaPersistsWhenTurnReturns()
    {
        string roomCode;
        long original, thief;
        CSharpFunctionalExtensions.Result<ComodinUsedDto> ruleta;
        GameSessionDocument s;
        // Si la ruleta cae en 3 elimina todas las incorrectas y el ladrón ya no puede fallar: repetir
        do
        {
            roomCode = await StartRoomAsync();
            s = await SessionAsync(roomCode);
            original = s.GetCurrentPlayerId()!.Value;
            thief = Bystander(s);
            await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id);

            ruleta = await UseAsync(roomCode, thief, ComodinTipo.Ruleta, Current(s).Id);
            Assert.True(ruleta.IsSuccess);
        } while (ruleta.Value.EliminatedAnswerIndexes!.Count == 3);

        s = await SessionAsync(roomCode);
        await _service.SubmitAnswerAsync(roomCode, thief, Current(s).Id, WrongIndex(s));

        var rejoin = await _service.GetRejoinStateAsync(roomCode);
        Assert.Equal(original, rejoin!.Turn!.CurrentPlayerId);
        Assert.Equal(ruleta.Value.EliminatedAnswerIndexes, rejoin.Turn.EliminatedAnswerIndexes);
    }

    [Fact]
    public async Task Robo_KickingActiveThief_ReturnsTurnToOriginal()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var original = s.GetCurrentPlayerId()!.Value;
        var thief = Bystander(s);
        await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id);

        Assert.True((await _service.KickPlayerAsync(roomCode, Owner, thief)).IsSuccess);

        var after = await SessionAsync(roomCode);
        Assert.False(after.StealActive);
        Assert.Equal(original, after.GetAnsweringPlayerId());
        Assert.NotNull(after.TurnDeadlineUnixMs);
    }

    // ========== Apuesta ==========

    [Fact]
    public async Task Apuesta_Resolved_WinnersGetHalfQuestionLosersNothing()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var original = s.GetCurrentPlayerId()!.Value;
        var winner = s.TurnQueue[1];
        var loser = s.TurnQueue[2];

        Assert.True((await UseAsync(roomCode, winner, ComodinTipo.Apuesta, Current(s).Id, true)).IsSuccess);
        Assert.True((await UseAsync(roomCode, loser, ComodinTipo.Apuesta, Current(s).Id, false)).IsSuccess);

        var result = await _service.SubmitAnswerAsync(roomCode, original, Current(s).Id, CorrectIndex(s));

        var bets = result!.Bets!;
        Assert.Equal(2, bets.Count);
        Assert.True(bets.Single(b => b.UserId == winner).Won);
        Assert.Equal(50, bets.Single(b => b.UserId == winner).PointsEarned);
        Assert.False(bets.Single(b => b.UserId == loser).Won);
        var after = await SessionAsync(roomCode);
        Assert.Equal(50, after.Players.Single(p => p.UserId == winner).Score);
        Assert.Equal(0, after.Players.Single(p => p.UserId == loser).Score);
        Assert.Empty(after.Bets);
    }

    [Theory]
    [InlineData(ComodinTipo.Ruleta)]
    [InlineData(ComodinTipo.DobleONada)]
    [InlineData(ComodinTipo.Apuesta)]
    public async Task Robo_AfterAnyComodin_Fails(ComodinTipo previo)
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var original = s.GetCurrentPlayerId()!.Value;
        var user = previo == ComodinTipo.Apuesta ? s.TurnQueue[1] : original;
        var thief = s.TurnQueue[2];

        Assert.True((await UseAsync(roomCode, user, previo, Current(s).Id, previo == ComodinTipo.Apuesta ? true : null)).IsSuccess);

        Assert.True((await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id)).IsFailure);
        Assert.False((await SessionAsync(roomCode)).StealActive);
    }

    [Fact]
    public async Task Robo_StillAllowedAgain_OnNextQuestion()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var thief = s.TurnQueue[2];
        await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.DobleONada, Current(s).Id);
        await _service.SubmitAnswerAsync(roomCode, s.GetCurrentPlayerId()!.Value, Current(s).Id, CorrectIndex(s));

        s = await SessionAsync(roomCode);
        Assert.False(s.ComodinUsedOnQuestion);
        Assert.True((await UseAsync(roomCode, thief == s.GetCurrentPlayerId() ? Bystander(s) : thief, ComodinTipo.Robo, Current(s).Id)).IsSuccess);
    }

    [Fact]
    public async Task Apuesta_InvalidCases_Fail()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var original = s.GetCurrentPlayerId()!.Value;
        var bettor = s.TurnQueue[1];

        Assert.True((await UseAsync(roomCode, original, ComodinTipo.Apuesta, Current(s).Id, true)).IsFailure);
        Assert.True((await UseAsync(roomCode, bettor, ComodinTipo.Apuesta, Current(s).Id)).IsFailure); // sin predicción

        // Quien apuesta no puede robar esa pregunta
        Assert.True((await UseAsync(roomCode, bettor, ComodinTipo.Apuesta, Current(s).Id, true)).IsSuccess);
        Assert.True((await UseAsync(roomCode, bettor, ComodinTipo.Robo, Current(s).Id)).IsFailure);
    }

    [Fact]
    public async Task Apuesta_DuringOrAfterSteal_Fails()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var thief = s.TurnQueue[2];

        // Ni se puede apostar durante un robo, ni el ladrón puede apostar tras fallarlo
        Assert.True((await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id)).IsSuccess);
        Assert.True((await UseAsync(roomCode, s.TurnQueue[1], ComodinTipo.Apuesta, Current(s).Id, true)).IsFailure);
        await _service.SubmitAnswerAsync(roomCode, thief, Current(s).Id, WrongIndex(s));
        Assert.True((await UseAsync(roomCode, thief, ComodinTipo.Apuesta, Current(s).Id, true)).IsFailure);
    }

    // ========== Pasar ==========

    [Fact]
    public async Task Pasar_ClosesTurnWithoutPoints_AdvancesQuestionAndEmitsPassedResult()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var passer = s.GetCurrentPlayerId()!.Value;
        var firstQuestion = Current(s).Id;

        var result = await UseAsync(roomCode, passer, ComodinTipo.Pasar, firstQuestion);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("Pasar", result.Value.AvailableComodines);
        var after = await SessionAsync(roomCode);
        var player = after.Players.Single(p => p.UserId == passer);
        Assert.Equal(0, player.Score);
        Assert.Equal(0, player.CorrectAnswers);
        Assert.Equal(0, player.WrongAnswers);
        Assert.Equal(1, after.CurrentQuestionIndex);
        Assert.NotEqual(firstQuestion, Current(after).Id);
        Assert.NotEqual(passer, after.GetCurrentPlayerId());

        var turnResult = Assert.IsType<TurnResultDto>(_sent.Single(m => m.Method == "TurnResult").Payload);
        Assert.True(turnResult.Passed);
        Assert.Equal(passer, turnResult.PlayerId);
        Assert.Equal(0, turnResult.PointsEarned);
        Assert.Contains(_sent, m => m.Method == "TurnStarted");
    }

    [Fact]
    public async Task Pasar_RefundsBetsOnThatQuestion()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var bettor = s.TurnQueue[1];
        Assert.True((await UseAsync(roomCode, bettor, ComodinTipo.Apuesta, Current(s).Id, true)).IsSuccess);

        await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.Pasar, Current(s).Id);

        var after = await SessionAsync(roomCode);
        Assert.Empty(after.Bets);
        Assert.Contains(ComodinTipo.Apuesta.ToString(), after.Players.Single(p => p.UserId == bettor).AvailableComodines(after.Mode, after.ComodinUsos).Select(c => c.ToString()));
        var turnResult = (TurnResultDto)_sent.Single(m => m.Method == "TurnResult").Payload!;
        Assert.True(turnResult.Bets!.Single().Refunded);
    }

    [Fact]
    public async Task Pasar_OutOfTurn_Fails()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        var result = await UseAsync(roomCode, Bystander(s), ComodinTipo.Pasar, Current(s).Id);

        Assert.True(result.IsFailure);
        Assert.Equal(0, (await SessionAsync(roomCode)).CurrentQuestionIndex);
    }

    [Fact]
    public async Task Pasar_OnStolenQuestion_Fails()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var thief = Bystander(s);
        Assert.True((await UseAsync(roomCode, thief, ComodinTipo.Robo, Current(s).Id)).IsSuccess);

        var result = await UseAsync(roomCode, thief, ComodinTipo.Pasar, Current(s).Id);

        Assert.True(result.IsFailure);
        Assert.Equal(0, (await SessionAsync(roomCode)).CurrentQuestionIndex);
    }

    [Fact]
    public async Task Pasar_OnLastQuestion_EndsTheGame()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        s.CurrentQuestionIndex = s.Questions.Count - 1;
        await _store.SaveAsync(s);

        var result = await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.Pasar, Current(s).Id);

        Assert.True(result.IsSuccess);
        Assert.Contains(_sent, m => m.Method == "GameEnded" || m.Method == "GameFinished");
    }

    [Fact]
    public async Task Pasar_Presencial_LeavesResultOnScreenUntilHostAdvances()
    {
        var roomCode = await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner", null, GameMode.Presencial);
        foreach (var id in new[] { 200L, 300L, 400L })
        {
            await _service.JoinGameAsync(roomCode, id, $"p{id}", $"conn-{id}");
        }
        Assert.NotNull(await _service.StartGameAsync(roomCode, Owner));
        _sent.Clear();
        var s = await SessionAsync(roomCode);

        var result = await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.Pasar, Current(s).Id);

        Assert.True(result.IsSuccess);
        var after = await SessionAsync(roomCode);
        Assert.True(after.AwaitingNextQuestion);
        Assert.Equal(0, after.CurrentQuestionIndex);
        Assert.True(after.LastTurnResult!.Passed);
        Assert.True((await _service.NextQuestionAsync(roomCode, Owner)).IsSuccess);
        Assert.Equal(1, (await SessionAsync(roomCode)).CurrentQuestionIndex);
    }

    // ========== 50/50 ==========

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    public void CincuentaCincuenta_EliminatesHalfOfIncorrectRoundedDown(int incorrectas, int esperadas) =>
        Assert.Equal(esperadas, ComodinReglas.EliminadasCincuentaCincuenta(incorrectas));

    [Fact]
    public async Task CincuentaCincuenta_FourAnswers_EliminatesOneIncorrectAndNeverTheCorrect()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var roomCode = await StartRoomAsync();
            var s = await SessionAsync(roomCode);

            var result = await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.CincuentaCincuenta, Current(s).Id);

            Assert.True(result.IsSuccess);
            var eliminated = Assert.Single(result.Value.EliminatedAnswerIndexes!);
            Assert.NotEqual(CorrectIndex(s), eliminated);
            Assert.Equal([eliminated], (await SessionAsync(roomCode)).EliminatedAnswerIndexes);
            Assert.DoesNotContain("CincuentaCincuenta", result.Value.AvailableComodines);
        }
    }

    [Fact]
    public async Task CincuentaCincuenta_FiveIncorrect_EliminatesTwo()
    {
        _questions = Enumerable.Range(1, 3).Select(i => new Pregunta
        {
            Id = i,
            NumeroPregunta = i,
            Enunciado = $"Pregunta {i}",
            Respuestas = Enumerable.Range(0, 6)
                .Select(r => new Respuesta { Id = i * 10 + r, Texto = $"R{r}", EsCorrecta = r == 0 }).ToList()
        }).ToList();
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        var result = await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.CincuentaCincuenta, Current(s).Id);

        Assert.Equal(2, result.Value.EliminatedAnswerIndexes!.Count);
        Assert.DoesNotContain(CorrectIndex(s), result.Value.EliminatedAnswerIndexes);
    }

    [Fact]
    public async Task CincuentaCincuenta_OutOfTurn_Fails()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        var result = await UseAsync(roomCode, Bystander(s), ComodinTipo.CincuentaCincuenta, Current(s).Id);

        Assert.True(result.IsFailure);
    }

    // ========== Comodines configurables y usos ==========

    private async Task<string> StartConfiguredRoomAsync(Dictionary<string, int>? config)
    {
        var roomCode = await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner", null, GameMode.Normal, config);
        for (var i = 0; i < 3; i++)
        {
            var id = 200L + i * 100;
            await _service.JoinGameAsync(roomCode, id, $"p{id}", $"conn-{id}");
        }
        Assert.NotNull(await _service.StartGameAsync(roomCode, Owner));
        _sent.Clear();
        return roomCode;
    }

    [Fact]
    public void Normalizar_DropsUnknownDisabledAndModeInvalid_AndCapsUses()
    {
        var config = ComodinReglas.Normalizar(GameMode.Normal, new Dictionary<string, int>
        {
            ["Ruleta"] = 500, ["robo"] = 2, ["Nope"] = 1, ["Llamada"] = 1, ["Apuesta"] = 0
        });

        Assert.Equal(2, config!.Count);
        Assert.Equal(ComodinReglas.MaxUsos, config[ComodinTipo.Ruleta]);
        Assert.Equal(2, config[ComodinTipo.Robo]);
        Assert.Null(ComodinReglas.Normalizar(GameMode.Normal, null));
    }

    [Fact]
    public async Task Config_DisabledComodin_IsNotAvailableAndCannotBeUsed()
    {
        var roomCode = await StartConfiguredRoomAsync(new() { ["Ruleta"] = 1 });
        var s = await SessionAsync(roomCode);
        var player = s.Players.First(p => p.CanPlay());

        Assert.Equal([ComodinTipo.Ruleta], player.AvailableComodines(s.Mode, s.ComodinUsos));

        var result = await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.DobleONada, Current(s).Id);
        Assert.True(result.IsFailure);
        Assert.DoesNotContain(s.Players.Single(p => p.UserId == s.GetCurrentPlayerId()).UsedComodines, c => c == ComodinTipo.DobleONada);
    }

    [Fact]
    public async Task Config_NoConfig_KeepsAllComodinesOfTheModeWithOneUse()
    {
        var roomCode = await StartConfiguredRoomAsync(null);
        var s = await SessionAsync(roomCode);
        var player = s.Players.First(p => p.CanPlay());

        Assert.Equal(6, player.AvailableComodines(s.Mode, s.ComodinUsos).Count);
        Assert.All(player.RemainingUses(s.Mode, s.ComodinUsos).Values, v => Assert.Equal(1, v));
    }

    [Fact]
    public async Task Usos_ComodinWithTwoUses_SurvivesFirstUseAndFailsAfterSecond()
    {
        var roomCode = await StartConfiguredRoomAsync(new() { ["Ruleta"] = 2 });
        var s = await SessionAsync(roomCode);
        var turn = s.GetCurrentPlayerId()!.Value;

        var first = await UseAsync(roomCode, turn, ComodinTipo.Ruleta, Current(s).Id);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value.RemainingUses!["Ruleta"]);
        Assert.Contains("Ruleta", first.Value.AvailableComodines);

        // Segundo uso (lo habilitamos saltando el límite de un comodín por pregunta)
        s = await SessionAsync(roomCode);
        s.ComodinUsedOnQuestion = false;
        s.EliminatedAnswerIndexes.Clear();
        await _store.SaveAsync(s);
        var second = await UseAsync(roomCode, turn, ComodinTipo.Ruleta, Current(s).Id);
        Assert.True(second.IsSuccess);
        Assert.DoesNotContain("Ruleta", second.Value.AvailableComodines);

        s = await SessionAsync(roomCode);
        s.ComodinUsedOnQuestion = false;
        await _store.SaveAsync(s);
        var third = await UseAsync(roomCode, turn, ComodinTipo.Ruleta, Current(s).Id);
        Assert.True(third.IsFailure);
    }

    [Fact]
    public async Task ReviveComodin_WithSeveralUses_RestoresOneUseAtATime()
    {
        var roomCode = await StartConfiguredRoomAsync(new() { ["Ruleta"] = 3 });
        await MarkUsedAsync(roomCode, 200L, ComodinTipo.Ruleta, ComodinTipo.Ruleta);

        await _service.ReviveComodinAsync(roomCode, Owner, 200L, ComodinTipo.Ruleta);

        var s = await SessionAsync(roomCode);
        Assert.Single(s.Players.Single(p => p.UserId == 200L).UsedComodines);
    }

    // ========== Anfitrión revive comodines ==========

    private async Task MarkUsedAsync(string roomCode, long userId, params ComodinTipo[] usados)
    {
        var s = await SessionAsync(roomCode);
        s.Players.Single(p => p.UserId == userId).UsedComodines.AddRange(usados);
        await _store.SaveAsync(s);
    }

    [Fact]
    public async Task ReviveComodin_Owner_RestoresOneComodinAndBroadcastsPlayers()
    {
        var roomCode = await StartRoomAsync();
        await MarkUsedAsync(roomCode, 200L, ComodinTipo.Ruleta, ComodinTipo.Robo);

        var result = await _service.ReviveComodinAsync(roomCode, Owner, 200L, ComodinTipo.Ruleta);

        Assert.True(result.IsSuccess);
        var s = await SessionAsync(roomCode);
        Assert.Equal([ComodinTipo.Robo], s.Players.Single(p => p.UserId == 200L).UsedComodines);
        Assert.Contains(_sent, m => m.Method == "PlayersList");
    }

    [Fact]
    public async Task ReviveComodin_WithoutTipo_RestoresAllUsed()
    {
        var roomCode = await StartRoomAsync();
        await MarkUsedAsync(roomCode, 200L, ComodinTipo.Ruleta, ComodinTipo.Robo);

        var result = await _service.ReviveComodinAsync(roomCode, Owner, 200L, null);

        Assert.True(result.IsSuccess);
        Assert.Empty((await SessionAsync(roomCode)).Players.Single(p => p.UserId == 200L).UsedComodines);
    }

    [Fact]
    public async Task ReviveComodin_NotOwner_Fails()
    {
        var roomCode = await StartRoomAsync();
        await MarkUsedAsync(roomCode, 200L, ComodinTipo.Ruleta);

        var result = await _service.ReviveComodinAsync(roomCode, 300L, 200L, ComodinTipo.Ruleta);

        Assert.True(result.IsFailure);
        Assert.Contains(ComodinTipo.Ruleta, (await SessionAsync(roomCode)).Players.Single(p => p.UserId == 200L).UsedComodines);
    }

    [Fact]
    public async Task ReviveComodin_NothingToRevive_Fails()
    {
        var roomCode = await StartRoomAsync();

        var result = await _service.ReviveComodinAsync(roomCode, Owner, 200L, ComodinTipo.Ruleta);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ReviveComodin_InLobby_Fails()
    {
        var roomCode = await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner");
        await _service.JoinGameAsync(roomCode, 200L, "p200", "conn-200");

        var result = await _service.ReviveComodinAsync(roomCode, Owner, 200L, null);

        Assert.True(result.IsFailure);
    }

    // ========== Reglas generales ==========

    [Fact]
    public async Task Comodin_WhilePaused_Fails()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        Assert.True((await _service.PauseGameAsync(roomCode, Owner)).IsSuccess);

        Assert.True((await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.Ruleta, Current(s).Id)).IsFailure);
    }

    [Fact]
    public async Task Comodin_StaleQuestionId_Fails()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        Assert.True((await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.Ruleta, 999)).IsFailure);
    }

    [Fact]
    public async Task QuestionState_ResetsOnNextQuestion()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;
        await UseAsync(roomCode, player, ComodinTipo.DobleONada, Current(s).Id);
        await UseAsync(roomCode, Bystander(s), ComodinTipo.Apuesta, Current(s).Id, true);
        await _service.SubmitAnswerAsync(roomCode, player, Current(s).Id, CorrectIndex(s));

        var after = await SessionAsync(roomCode);
        Assert.Empty(after.DoubleOrNothingPlayers);
        Assert.Empty(after.Bets);
        Assert.Empty(after.EliminatedAnswerIndexes);
        Assert.Null(after.StolenById);
    }

    [Fact]
    public async Task PlayerDtos_ExposeAvailableComodines_OnlyForPlayers()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        await UseAsync(roomCode, s.GetCurrentPlayerId()!.Value, ComodinTipo.Ruleta, Current(s).Id);

        var players = (await _service.GetRejoinStateAsync(roomCode))!.GameState.Players;
        Assert.Empty(players.Single(p => p.UserId == Owner).AvailableComodines!);
        Assert.Equal(["DobleONada", "Robo", "Apuesta", "CincuentaCincuenta", "Pasar"], players.Single(p => p.UserId == s.GetCurrentPlayerId()).AvailableComodines);
        Assert.Equal(6, players.Single(p => p.UserId == Bystander(s)).AvailableComodines!.Count);
    }

    // ========== Bonus de tiempo en el servidor ==========

    [Fact]
    public async Task TimeBonus_IsComputedFromServerDeadline()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);
        var player = s.GetCurrentPlayerId()!.Value;

        // Quedan ~1,5 s reales → 1 s visible → 100 + 1*10
        s.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddMilliseconds(1500).ToUnixTimeMilliseconds();
        await _store.SaveAsync(s);

        var result = await _service.SubmitAnswerAsync(roomCode, player, Current(s).Id, CorrectIndex(s));

        Assert.Equal(110, result!.PointsEarned);
    }

    [Fact]
    public async Task TimeBonus_FreshTurn_GetsMaxBonus()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        var result = await _service.SubmitAnswerAsync(roomCode, s.GetCurrentPlayerId()!.Value, Current(s).Id, CorrectIndex(s));

        Assert.Equal(300, result!.PointsEarned); // 100 + min(20*10, 200)
    }
}
