using CSharpFunctionalExtensions;
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

namespace TriviUpTest.Services;

/// <summary>Pregunta especial de Colores: el que mejor imita el color se lleva la pregunta.</summary>
public class GameServiceColoresTests
{
    private const long Owner = 100L;
    private readonly InMemoryGameSessionStore _store = new();
    private readonly List<(string Method, object? Payload)> _sent = new();
    private readonly GameService _service;
    private List<Pregunta> _questions = Questions(TiposPregunta.Colores, TiposPregunta.Normal, TiposPregunta.Normal);

    public GameServiceColoresTests()
    {
        var groupProxy = new Mock<IClientProxy>();
        groupProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => _sent.Add((method, args.FirstOrDefault())))
            .Returns(Task.CompletedTask);

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(groupProxy.Object);
        hubClients.Setup(c => c.Client(It.IsAny<string>())).Returns(new Mock<ISingleClientProxy>().Object);
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

    /// <summary>Cada pregunta en su propia fase: el orden es fijo (dentro de una fase se barajan).</summary>
    private static List<Pregunta> Questions(params string[] tipos) =>
        tipos.Select((tipo, i) => new Pregunta
        {
            Id = i + 1,
            NumeroPregunta = i + 1,
            FaseNumero = i + 1,
            Enunciado = $"Pregunta {i + 1}",
            Tipo = tipo,
            Respuestas =
            [
                new Respuesta { Id = (i + 1) * 10 + 1, Texto = "Correcta", EsCorrecta = true },
                new Respuesta { Id = (i + 1) * 10 + 2, Texto = "Mal", EsCorrecta = false }
            ]
        }).ToList();

    private static readonly long[] PlayerIds = [200L, 300L, 400L];

    private async Task<string> StartRoomAsync(int players = 3, GameMode mode = GameMode.Normal)
    {
        var roomCode = await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner", 20, mode);
        foreach (var id in PlayerIds.Take(players))
        {
            await _service.JoinGameAsync(roomCode, id, $"p{id}", $"conn-{id}");
        }
        Assert.NotNull(await _service.StartGameAsync(roomCode, Owner));
        _sent.Clear();
        return roomCode;
    }

    private async Task<GameSessionDocument> SessionAsync(string roomCode) => (await _store.GetAsync(roomCode))!;

    private async Task<Result> SubmitAsync(string roomCode, long userId, ColorHsb color)
    {
        var s = await SessionAsync(roomCode);
        return await _service.SubmitColorAsync(roomCode, userId, s.Questions[s.CurrentQuestionIndex].Id, color);
    }

    private ColorChallengeResultDto ColorResult() =>
        Assert.IsType<ColorChallengeResultDto>(_sent.Single(m => m.Method == "ColorChallengeResult").Payload);

    // ========== Arranque de la prueba ==========

    [Fact]
    public async Task ColorQuestion_OpensChallengeWithRandomTargetAndNobodyAnswering()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        Assert.True(s.ColorOpen);
        Assert.NotNull(s.ColorTarget);
        Assert.True(s.ColorTarget!.IsValid());
        Assert.Null(s.GetCurrentPlayerId());
        Assert.Null(s.GetAnsweringPlayerId());
    }

    [Fact]
    public async Task ColorChallenge_Lasts60Seconds_EvenInPresencial()
    {
        var roomCode = await StartRoomAsync(mode: GameMode.Presencial);
        var s = await SessionAsync(roomCode);

        var remainingMs = s.TurnDeadlineUnixMs!.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.InRange(remainingMs, (ColorMatch.ChallengeSeconds - 2) * 1000, (ColorMatch.ChallengeSeconds + 1) * 1000);
    }

    [Fact]
    public async Task TurnStarted_IncludesTargetButNotOtherPlayersColors()
    {
        var roomCode = await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner", 20);
        await _service.JoinGameAsync(roomCode, 200L, "p200", "conn-200");
        await _service.JoinGameAsync(roomCode, 300L, "p300", "conn-300");
        await _service.StartGameAsync(roomCode, Owner);
        var s = await SessionAsync(roomCode);

        var turn = Assert.IsType<TurnStartedDto>(_sent.Last(m => m.Method == "TurnStarted").Payload);
        Assert.True(turn.IsColor);
        Assert.True(turn.IsDynamic);
        Assert.True(turn.ColorOpen);
        Assert.Equal(0, turn.CurrentPlayerId);
        Assert.Equal(s.ColorTarget, turn.ColorTarget);
        Assert.Empty(turn.ColorSubmittedPlayerIds!);
    }

    [Fact]
    public async Task EachColorQuestion_GetsItsOwnTarget()
    {
        // Con varias preguntas de colores, cada una sortea su color (probabilidad de repetir ínfima).
        _questions = Questions(TiposPregunta.Colores, TiposPregunta.Colores, TiposPregunta.Colores, TiposPregunta.Colores);
        var roomCode = await StartRoomAsync(players: 2);
        var targets = new List<ColorHsb>();

        for (var i = 0; i < 4; i++)
        {
            var s = await SessionAsync(roomCode);
            targets.Add(s.ColorTarget!);
            await _service.NextQuestionAsync(roomCode, Owner);
            await ContinueIfPhaseBreakAsync(roomCode);
        }

        Assert.True(targets.Distinct().Count() > 1);
    }

    // ========== Envío y resolución ==========

    [Fact]
    public async Task Submit_NotAllIn_KeepsChallengeOpenAndAnnouncesWhoSubmitted()
    {
        var roomCode = await StartRoomAsync();

        var result = await SubmitAsync(roomCode, 200L, new ColorHsb(10, 50, 50));

        Assert.True(result.IsSuccess);
        var s = await SessionAsync(roomCode);
        Assert.True(s.ColorOpen);
        var dto = Assert.IsType<ColorSubmittedDto>(_sent.Single(m => m.Method == "ColorSubmitted").Payload);
        Assert.Equal(200L, dto.PlayerId);
    }

    [Fact]
    public async Task Submit_Twice_Fails()
    {
        var roomCode = await StartRoomAsync();
        await SubmitAsync(roomCode, 200L, new ColorHsb(10, 50, 50));

        var result = await SubmitAsync(roomCode, 200L, new ColorHsb(20, 50, 50));

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(360, 50, 50)]
    [InlineData(-1, 50, 50)]
    [InlineData(10, 101, 50)]
    [InlineData(10, 50, -5)]
    public async Task Submit_InvalidColor_Fails(int h, int s, int b)
    {
        var roomCode = await StartRoomAsync();

        Assert.False((await SubmitAsync(roomCode, 200L, new ColorHsb(h, s, b))).IsSuccess);
    }

    [Fact]
    public async Task Submit_ByOwnerOrOnNormalQuestion_Fails()
    {
        var roomCode = await StartRoomAsync();
        Assert.False((await SubmitAsync(roomCode, Owner, new ColorHsb(10, 50, 50))).IsSuccess);

        _questions = Questions(TiposPregunta.Normal, TiposPregunta.Normal);
        var normalRoom = await StartRoomAsync(players: 2);
        Assert.False((await SubmitAsync(normalRoom, 200L, new ColorHsb(10, 50, 50))).IsSuccess);
    }

    [Fact]
    public async Task AllSubmitted_ClosestWinsAndAnswersTheQuestion()
    {
        var roomCode = await StartRoomAsync();
        var target = (await SessionAsync(roomCode)).ColorTarget!;
        var cerca = target with { Brightness = Math.Max(0, target.Brightness - 3) };
        var lejos = new ColorHsb((target.Hue + 180) % 360, target.Saturation, target.Brightness);
        var exacto = target;

        await SubmitAsync(roomCode, 200L, cerca);
        await SubmitAsync(roomCode, 300L, lejos);
        await SubmitAsync(roomCode, 400L, exacto);

        var s = await SessionAsync(roomCode);
        Assert.False(s.ColorOpen);
        Assert.Equal(400L, s.GetCurrentPlayerId());

        var result = ColorResult();
        Assert.Equal(400L, result.WinnerId);
        Assert.False(result.TieBroken);
        Assert.Equal([400L, 200L, 300L], result.Guesses.Select(g => g.PlayerId));
        Assert.Equal(100, result.Guesses[0].Similarity);

        var turn = Assert.IsType<TurnStartedDto>(_sent.Last(m => m.Method == "TurnStarted").Payload);
        Assert.Equal(400L, turn.CurrentPlayerId);
        Assert.False(turn.ColorOpen);
        Assert.Null(turn.ColorTarget);
    }

    [Fact]
    public async Task Winner_AnswersCorrectly_GetsPoints_AndQueueDoesNotAdvance()
    {
        var roomCode = await StartRoomAsync();
        var target = (await SessionAsync(roomCode)).ColorTarget!;
        var colaAntes = (await SessionAsync(roomCode)).TurnQueue.ToList();
        await SubmitAsync(roomCode, 200L, target);
        await SubmitAsync(roomCode, 300L, target with { Hue = (target.Hue + 90) % 360 });
        await SubmitAsync(roomCode, 400L, target with { Hue = (target.Hue + 180) % 360 });

        var s = await SessionAsync(roomCode);
        var turn = await _service.SubmitAnswerAsync(roomCode, 200L, s.Questions[0].Id, s.Questions[0].Respuestas.FindIndex(r => r.EsCorrecta));

        Assert.NotNull(turn);
        Assert.True(turn!.IsCorrect);
        await ContinueIfPhaseBreakAsync(roomCode);
        s = await SessionAsync(roomCode);
        Assert.Equal(1, s.CurrentQuestionIndex);
        // La pregunta de colores no gasta turno: responde el primero de la cola.
        Assert.Equal(colaAntes[0], s.GetCurrentPlayerId());
    }

    [Fact]
    public async Task Tie_WinnerChosenAmongTiedOnly()
    {
        var winners = new HashSet<long>();
        for (var attempt = 0; attempt < 40 && winners.Count < 2; attempt++)
        {
            _sent.Clear();
            var roomCode = await StartRoomAsync();
            var target = (await SessionAsync(roomCode)).ColorTarget!;
            await SubmitAsync(roomCode, 200L, target);
            await SubmitAsync(roomCode, 300L, target);
            await SubmitAsync(roomCode, 400L, target with { Hue = (target.Hue + 180) % 360 });

            var result = ColorResult();
            Assert.True(result.TieBroken);
            Assert.Contains(result.WinnerId!.Value, new[] { 200L, 300L });
            Assert.Equal(result.WinnerId, result.Guesses[0].PlayerId);
            winners.Add(result.WinnerId.Value);
        }

        Assert.Equal(2, winners.Count);
    }

    [Fact]
    public async Task Timeout_ResolvesWithSubmittedColorsOnly()
    {
        var roomCode = await StartRoomAsync();
        var target = (await SessionAsync(roomCode)).ColorTarget!;
        await SubmitAsync(roomCode, 300L, target with { Hue = (target.Hue + 30) % 360 });

        await ForceDeadlineAsync(roomCode);

        var result = ColorResult();
        Assert.Equal(300L, result.WinnerId);
        Assert.Single(result.Guesses);
        Assert.Equal(300L, (await SessionAsync(roomCode)).GetCurrentPlayerId());
    }

    [Fact]
    public async Task Timeout_NobodySubmitted_SkipsQuestion()
    {
        var roomCode = await StartRoomAsync();

        await ForceDeadlineAsync(roomCode);

        var result = ColorResult();
        Assert.Null(result.WinnerId);
        Assert.Empty(result.Guesses);
        var s = await SessionAsync(roomCode);
        Assert.Equal(1, s.CurrentQuestionIndex);
        Assert.False(s.ColorOpen);
    }

    [Fact]
    public async Task HostCanSkipColorQuestion()
    {
        var roomCode = await StartRoomAsync();

        Assert.True((await _service.NextQuestionAsync(roomCode, Owner)).IsSuccess);

        var s = await SessionAsync(roomCode);
        Assert.Equal(1, s.CurrentQuestionIndex);
        Assert.False(s.ColorOpen);
    }

    [Fact]
    public async Task Comodines_NotAllowedDuringColorQuestion()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        var result = await _service.UseComodinAsync(roomCode, 200L, ComodinTipo.Robo, s.Questions[0].Id);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Kick_LastPendingPlayer_ResolvesChallenge()
    {
        var roomCode = await StartRoomAsync();
        var target = (await SessionAsync(roomCode)).ColorTarget!;
        await SubmitAsync(roomCode, 200L, target);
        await SubmitAsync(roomCode, 300L, target with { Hue = (target.Hue + 90) % 360 });

        await _service.KickPlayerAsync(roomCode, Owner, 400L);

        Assert.Equal(200L, ColorResult().WinnerId);
        Assert.Equal(200L, (await SessionAsync(roomCode)).GetCurrentPlayerId());
    }

    [Fact]
    public async Task Kick_Winner_NextClosestTakesTheQuestion()
    {
        var roomCode = await StartRoomAsync();
        var target = (await SessionAsync(roomCode)).ColorTarget!;
        await SubmitAsync(roomCode, 200L, target);
        await SubmitAsync(roomCode, 300L, target with { Hue = (target.Hue + 20) % 360 });
        await SubmitAsync(roomCode, 400L, target with { Hue = (target.Hue + 180) % 360 });
        _sent.Clear();

        await _service.KickPlayerAsync(roomCode, Owner, 200L);

        Assert.Equal(300L, ColorResult().WinnerId);
        Assert.Equal(300L, (await SessionAsync(roomCode)).GetCurrentPlayerId());
    }

    [Fact]
    public async Task PauseAndResume_KeepsChallengeDeadline_InPresencial()
    {
        var roomCode = await StartRoomAsync(mode: GameMode.Presencial);

        Assert.True((await _service.PauseGameAsync(roomCode, Owner)).IsSuccess);
        Assert.True((await _service.ResumeGameAsync(roomCode, Owner)).IsSuccess);

        var s = await SessionAsync(roomCode);
        Assert.True(s.ColorOpen);
        Assert.NotNull(s.TurnDeadlineUnixMs);
    }

    [Fact]
    public async Task FairTurns_ColorQuestionsDoNotCount()
    {
        // 3 preguntas por turno + 1 de colores con 2 jugadores: se recorta 1 por turno y se queda la de colores.
        _questions = Questions(TiposPregunta.Colores, TiposPregunta.Normal, TiposPregunta.Normal, TiposPregunta.Normal);
        var roomCode = await StartRoomAsync(players: 2);

        var s = await SessionAsync(roomCode);
        Assert.Equal(3, s.Questions.Count);
        Assert.True(s.Questions[0].EsColores);
    }

    private async Task ContinueIfPhaseBreakAsync(string roomCode)
    {
        if ((await SessionAsync(roomCode)).State == GameState.PhaseBreak)
        {
            Assert.True((await _service.ContinuePhaseAsync(roomCode, Owner)).IsSuccess);
        }
    }

    private async Task ForceDeadlineAsync(string roomCode)
    {
        var s = await SessionAsync(roomCode);
        s.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
        await _store.SaveAsync(s);
        await _service.ProcessDueTimeoutAsync(roomCode, s.TurnGeneration);
    }
}
