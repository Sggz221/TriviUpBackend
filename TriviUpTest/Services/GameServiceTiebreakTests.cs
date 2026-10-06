using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.Entities;
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

/// <summary>Desempate del podio: tanda de penaltis por el 1º y sorteos (moneda o ruleta) para el resto.</summary>
public class GameServiceTiebreakTests
{
    private const long Owner = 100L;
    private const long Autor = 7L;
    private readonly InMemoryGameSessionStore _store = new();
    private readonly Mock<IClientProxy> _groupProxy = new();
    private readonly List<(string Method, object? Payload)> _sent = new();
    private readonly List<BancoPregunta> _banco = [];
    private readonly GameService _service;

    public GameServiceTiebreakTests()
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
            .ReturnsAsync((long id) => new Quiz { Id = id, Nombre = "Test Quiz", CreatorId = Autor });
        quizRepository.Setup(r => r.GetQuestionsWithAnswersAsync(It.IsAny<long>()))
            .ReturnsAsync(() => Enumerable.Range(1, 2).Select(i => new Pregunta
            {
                Id = i,
                NumeroPregunta = i,
                Enunciado = $"Pregunta {i}",
                Respuestas =
                [
                    new Respuesta { Id = i * 10 + 1, Texto = "Correcta", EsCorrecta = true },
                    new Respuesta { Id = i * 10 + 2, Texto = "Mal", EsCorrecta = false }
                ]
            }).ToList());

        var banco = new Mock<IBancoPreguntaRepository>();
        banco.Setup(r => r.FindIdsAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string?>()))
            .ReturnsAsync((long creator, long? _, string? _) => _banco.Where(p => p.CreatorId == creator).Select(p => p.Id).ToList());
        banco.Setup(r => r.FindByIdsAsync(It.IsAny<long>(), It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync((long creator, IReadOnlyCollection<long> ids) =>
                _banco.Where(p => p.CreatorId == creator && ids.Contains(p.Id)).ToList());

        var provider = new Mock<IServiceProvider>();
        provider.Setup(sp => sp.GetService(typeof(IHubContext<GameHub>))).Returns(hubContext.Object);
        provider.Setup(sp => sp.GetService(typeof(IQuizRepository))).Returns(quizRepository.Object);
        provider.Setup(sp => sp.GetService(typeof(IBancoPreguntaRepository))).Returns(banco.Object);
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

    private void FillBank(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            _banco.Add(new BancoPregunta
            {
                Id = 1000 + i, CreatorId = Autor, Enunciado = $"Banco {i}",
                Respuestas = [new BancoRespuesta { Texto = "Sí", EsCorrecta = true }, new BancoRespuesta { Texto = "No" }]
            });
        }
    }

    /// <summary>Partida de N jugadores en su última pregunta con las puntuaciones dadas.</summary>
    private async Task<string> LastQuestionWithScoresAsync(GameMode mode, params int[] scores)
    {
        var roomCode = await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner", null, mode);
        for (var i = 0; i < scores.Length; i++)
        {
            var id = 200L + i * 100;
            await _service.JoinGameAsync(roomCode, id, $"p{id}", $"conn-{id}");
        }
        Assert.NotNull(await _service.StartGameAsync(roomCode, Owner));

        var s = (await _store.GetAsync(roomCode))!;
        for (var i = 0; i < scores.Length; i++)
        {
            s.Players.Single(p => p.UserId == 200L + i * 100).Score = scores[i];
        }
        s.CurrentQuestionIndex = s.Questions.Count - 1;
        await _store.SaveAsync(s);
        _sent.Clear();
        return roomCode;
    }

    private async Task<GameSessionDocument> SessionAsync(string roomCode) => (await _store.GetAsync(roomCode))!;

    private static QuestionSnapshot Current(GameSessionDocument s) => s.Questions[s.CurrentQuestionIndex];

    /// <summary>Quien responde ahora contesta (bien o mal) en modo normal.</summary>
    private async Task AnswerAsync(string roomCode, bool correct)
    {
        var s = await SessionAsync(roomCode);
        var q = Current(s);
        var index = q.Respuestas.FindIndex(r => r.EsCorrecta == correct);
        Assert.NotNull(await _service.SubmitAnswerAsync(roomCode, s.GetAnsweringPlayerId()!.Value, q.Id, index));
    }

    private GameResultDto Finished() => (GameResultDto)_sent.Single(m => m.Method == "GameFinished").Payload!;

    // ========== Reglas de la tanda ==========

    private static ShootoutDocument Shootout(params long[] players) => new() { PlayerIds = players.ToList(), Started = true };

    private static void Kicks(ShootoutDocument s, params bool[] goals)
    {
        foreach (var goal in goals)
        {
            PenaltyShootout.RecordKick(s, PenaltyShootout.NextKicker(s)!.Value, goal);
        }
    }

    [Fact]
    public void Shootout_AlternatesKickers()
    {
        var s = Shootout(1, 2, 3);
        var order = new List<long>();
        for (var i = 0; i < 6; i++)
        {
            var kicker = PenaltyShootout.NextKicker(s)!.Value;
            order.Add(kicker);
            PenaltyShootout.RecordKick(s, kicker, true);
        }
        Assert.Equal([1, 2, 3, 1, 2, 3], order);
    }

    [Fact]
    public void Shootout_EndsEarly_WhenTheLeadCannotBeCaught()
    {
        var s = Shootout(1, 2);
        // A marca 3 y B falla 3: tras el 3º de B, B ya no puede llegar (0 + 2 < 3)
        Kicks(s, true, false, true, false, true, false);

        Assert.True(s.Finished);
        Assert.Equal(1, s.WinnerId);
        Assert.Equal(6, s.Kicks.Count);
    }

    [Fact]
    public void Shootout_GoesToSuddenDeath_AfterFiveEachTied_AndMissAfterGoalLoses()
    {
        var s = Shootout(1, 2);
        Kicks(s, Enumerable.Repeat(true, 10).ToArray());
        Assert.False(s.Finished);
        Assert.True(PenaltyShootout.InSuddenDeath(s));

        Kicks(s, true, false);

        Assert.True(s.Finished);
        Assert.Equal(1, s.WinnerId);
    }

    [Fact]
    public void Shootout_SuddenDeath_ContinuesWhenBothMiss()
    {
        var s = Shootout(1, 2);
        Kicks(s, Enumerable.Repeat(true, 10).ToArray());
        Kicks(s, false, false);
        Assert.False(s.Finished);
        Kicks(s, false, true);
        Assert.Equal(2, s.WinnerId);
    }

    [Fact]
    public void Shootout_ThreePlayers_EliminatesWhoCannotReachTheLeader()
    {
        var s = Shootout(1, 2, 3);
        // Rondas 1-3: A marca todo, B y C fallan todo → tras el 3º de C, B y C (0 + 2 < 3) quedan fuera
        Kicks(s, true, false, false, true, false, false, true, false, false);
        Assert.True(s.Finished);
        Assert.Equal(1, s.WinnerId);
    }

    [Fact]
    public void Shootout_Tiers_OrderEliminatedByRoundThenGoals()
    {
        var s = Shootout(1, 2, 3);
        // A y B marcan todo; C falla todo → C queda fuera tras su 3º tiro (0 + 2 < 3)
        Kicks(s, true, true, false, true, true, false, true, true, false);
        Assert.Contains(3L, s.EliminatedInRound.Keys);

        // Rondas 4 y 5 solo entre A y B: 5-5 → muerte súbita, A falla y B marca → gana B
        Kicks(s, true, true, true, true);
        Assert.False(s.Finished);
        Kicks(s, false, true);

        var tiers = PenaltyShootout.Tiers(s);
        Assert.Equal(2, s.WinnerId);
        Assert.Equal([2L], tiers[0]);
        Assert.Equal([1L], tiers[1]);
        Assert.Equal([3L], tiers[2]);
    }

    // ========== Clasificación y sorteos ==========

    private static List<PlayerDocument> Players(params int[] scores) =>
        scores.Select((score, i) => new PlayerDocument { UserId = i + 1, Username = $"j{i + 1}", Score = score }).ToList();

    [Fact]
    public void Ranking_TieForSecond_IsDrawnWithACoin()
    {
        var (results, tiebreaks) = FinalRanking.Build(Players(300, 200, 200, 100), null, new Random(1));

        Assert.Equal([1, 2, 3, 4], results.Select(r => r.Rank));
        var draw = Assert.Single(tiebreaks);
        Assert.Equal(FinalRanking.Moneda, draw.Kind);
        Assert.Equal(2, draw.Position);
        Assert.Equal(draw.PlayerIds, results.Skip(1).Take(2).Select(r => r.UserId));
    }

    [Fact]
    public void Ranking_TieAcrossThirdAndFourth_DrawDecidesThird_OthersShareFourth()
    {
        var (results, tiebreaks) = FinalRanking.Build(Players(300, 200, 100, 100, 100), null, new Random(2));

        Assert.Equal([1, 2, 3, 4, 4], results.Select(r => r.Rank));
        var draw = Assert.Single(tiebreaks);
        Assert.Equal(FinalRanking.Ruleta, draw.Kind);
        Assert.Equal(3, draw.Position);
        Assert.Equal(1, draw.Picks);
    }

    [Fact]
    public void Ranking_TiesBelowThePodium_ShareTheirPosition()
    {
        var (results, tiebreaks) = FinalRanking.Build(Players(400, 300, 200, 100, 100), null, new Random(3));

        Assert.Equal([1, 2, 3, 4, 4], results.Select(r => r.Rank));
        Assert.Empty(tiebreaks);
    }

    // ========== Flujo de la partida ==========

    [Fact]
    public async Task TieForFirst_WithEnoughBankQuestions_OpensExtraRound()
    {
        FillBank(12);
        var roomCode = await LastQuestionWithScoresAsync(GameMode.Normal, 100, 100);

        await AnswerAsync(roomCode, correct: false);

        var s = await SessionAsync(roomCode);
        Assert.Equal(GameState.PhaseBreak, s.State);
        Assert.NotNull(s.Shootout);
        var phase = (PhaseCompletedDto)_sent.Single(m => m.Method == "PhaseCompleted").Payload!;
        Assert.True(phase.IsExtraRound);
        Assert.Equal(2, phase.TiedPlayerIds!.Count);
        Assert.DoesNotContain(_sent, m => m.Method == "GameFinished");
    }

    [Fact]
    public async Task TieForFirst_WithoutEnoughBankQuestions_FinishesWithACoin()
    {
        FillBank(9); // hacen falta 10 (5 por jugador)
        var roomCode = await LastQuestionWithScoresAsync(GameMode.Normal, 100, 100);

        await AnswerAsync(roomCode, correct: false);

        var result = Finished();
        Assert.Equal([1, 2], result.PlayerResults.Select(r => r.Rank));
        Assert.Equal(FinalRanking.Moneda, Assert.Single(result.Tiebreaks!).Kind);
    }

    [Fact]
    public async Task Shootout_FullFlow_GoalsDoNotScore_ComodinesBlocked_WinnerIsFirst()
    {
        FillBank(12);
        var roomCode = await LastQuestionWithScoresAsync(GameMode.Normal, 100, 100);
        await AnswerAsync(roomCode, correct: false);

        Assert.True((await _service.ContinuePhaseAsync(roomCode, Owner)).IsSuccess);
        var s = await SessionAsync(roomCode);
        var first = s.Shootout!.PlayerIds[0];
        var turn = (TurnStartedDto)_sent.Last(m => m.Method == "TurnStarted").Payload!;
        Assert.NotNull(turn.Penalty);
        Assert.Equal(first, s.GetAnsweringPlayerId());

        var comodin = await _service.UseComodinAsync(roomCode, first, ComodinTipo.Ruleta, Current(s).Id);
        Assert.True(comodin.IsFailure);

        // El primero marca los 3 y el segundo falla los 3: se acaba antes de las 5 rondas
        for (var i = 0; i < 3; i++)
        {
            await AnswerAsync(roomCode, correct: true);
            await AnswerAsync(roomCode, correct: false);
        }

        var result = Finished();
        Assert.Equal(first, result.PlayerResults[0].UserId);
        Assert.All(result.PlayerResults, r => Assert.Equal(100, r.FinalScore));
        Assert.Equal(2, result.TotalQuestions);
        var penalties = Assert.Single(result.Tiebreaks!);
        Assert.Equal(FinalRanking.Penaltis, penalties.Kind);
        Assert.Equal(6, penalties.Kicks!.Count);
        Assert.Contains(_sent, m => m.Method == "PenaltyShootoutFinished");
    }

    [Fact]
    public async Task Shootout_OutOfQuestionsInSuddenDeath_DrawsTheWinner()
    {
        FillBank(10); // justo las 5 rondas; no hay para la muerte súbita
        var roomCode = await LastQuestionWithScoresAsync(GameMode.Normal, 100, 100);
        await AnswerAsync(roomCode, correct: false);
        Assert.True((await _service.ContinuePhaseAsync(roomCode, Owner)).IsSuccess);

        for (var i = 0; i < 10; i++) await AnswerAsync(roomCode, correct: true);

        var result = Finished();
        Assert.Equal([1, 2], result.PlayerResults.Select(r => r.Rank));
        Assert.True(result.Tiebreaks![0].OutOfQuestions);
        Assert.Equal(FinalRanking.Moneda, result.Tiebreaks[1].Kind);
    }

    [Fact]
    public async Task Shootout_Presencial_WaitsForHostBetweenKicks()
    {
        FillBank(12);
        var roomCode = await LastQuestionWithScoresAsync(GameMode.Presencial, 100, 100);
        var s = await SessionAsync(roomCode);
        var q = Current(s);
        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, q.Id, q.Respuestas.FindIndex(r => !r.EsCorrecta))).IsSuccess);
        Assert.True((await _service.ConfirmAnswerAsync(roomCode, Owner, q.Id)).IsSuccess);
        Assert.True((await _service.NextQuestionAsync(roomCode, Owner)).IsSuccess);
        Assert.True((await _service.ContinuePhaseAsync(roomCode, Owner)).IsSuccess);

        s = await SessionAsync(roomCode);
        q = Current(s);
        Assert.True((await _service.MarkAnswerAsync(roomCode, Owner, q.Id, q.Respuestas.FindIndex(r => r.EsCorrecta))).IsSuccess);
        var kick = await _service.ConfirmAnswerAsync(roomCode, Owner, q.Id);

        Assert.True(kick.Value.IsPenalty);
        s = await SessionAsync(roomCode);
        Assert.True(s.AwaitingNextQuestion);
        Assert.Single(s.Shootout!.Kicks);
        Assert.True((await _service.NextQuestionAsync(roomCode, Owner)).IsSuccess);
        Assert.Equal(s.Shootout.PlayerIds[1], (await SessionAsync(roomCode)).GetAnsweringPlayerId());
    }
}
