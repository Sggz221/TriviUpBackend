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

/// <summary>Pregunta especial Ocarina: el primero que toca la melodía se lleva la pregunta.</summary>
public class GameServiceOcarinaTests
{
    private const long Owner = 100L;
    private readonly InMemoryGameSessionStore _store = new();
    private readonly List<(string Method, object? Payload)> _sent = new();
    private readonly GameService _service;
    private List<Pregunta> _questions = Questions(TiposPregunta.Ocarina, TiposPregunta.Normal, TiposPregunta.Normal);

    public GameServiceOcarinaTests()
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

    private async Task<string> StartRoomAsync(GameMode mode = GameMode.Normal, bool skipListening = true)
    {
        var roomCode = await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner", 20, mode);
        await _service.JoinGameAsync(roomCode, 200L, "p200", "conn-200");
        await _service.JoinGameAsync(roomCode, 300L, "p300", "conn-300");
        Assert.NotNull(await _service.StartGameAsync(roomCode, Owner));
        if (skipListening) await FinishListeningAsync(roomCode);
        _sent.Clear();
        return roomCode;
    }

    /// <summary>Simula que la melodía ya ha terminado de sonar.</summary>
    private async Task FinishListeningAsync(string roomCode)
    {
        var s = await SessionAsync(roomCode);
        s.OcarinaListenUntilUnixMs = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
        await _store.SaveAsync(s);
    }

    private async Task<GameSessionDocument> SessionAsync(string roomCode) => (await _store.GetAsync(roomCode))!;

    private async Task<List<int>> MelodyAsync(string roomCode) =>
        (await SessionAsync(roomCode)).OcarinaMelody!.Select(n => n.Pitch).ToList();

    private async Task<Result<bool>> PlayAsync(string roomCode, long userId, IReadOnlyList<int> notes)
    {
        var s = await SessionAsync(roomCode);
        return await _service.SubmitOcarinaAsync(roomCode, userId, s.Questions[s.CurrentQuestionIndex].Id, notes);
    }

    private static List<int> Wrong(List<int> melody) => melody.Select(p => (p + 1) % OcarinaMelody.PitchCount).ToList();

    [Fact]
    public async Task OcarinaQuestion_OpensWithRandomMelody_NoTimeLimit_AndNobodyAnswering()
    {
        var roomCode = await StartRoomAsync(skipListening: false);
        var s = await SessionAsync(roomCode);

        Assert.True(s.OcarinaOpen);
        Assert.Equal(OcarinaMelody.Length, s.OcarinaMelody!.Count);
        Assert.Null(s.GetCurrentPlayerId());
        Assert.Null(s.TurnDeadlineUnixMs);
        Assert.True(s.OcarinaListenUntilUnixMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task TurnStarted_IncludesMelodyAndListeningTime()
    {
        await _service.CreateGameAsync(1L, Owner, "owner", "conn-owner", 20);
        var roomCode = (await _store.GetUserRoomAsync(Owner))!;
        await _service.JoinGameAsync(roomCode, 200L, "p200", "conn-200");
        await _service.StartGameAsync(roomCode, Owner);
        var s = await SessionAsync(roomCode);

        var turn = Assert.IsType<TurnStartedDto>(_sent.Last(m => m.Method == "TurnStarted").Payload);
        Assert.True(turn.IsOcarina);
        Assert.True(turn.IsDynamic);
        Assert.True(turn.OcarinaOpen);
        Assert.Equal(0, turn.CurrentPlayerId);
        Assert.Equal(0, turn.TimeLimit);
        Assert.Equal(s.OcarinaMelody!.Select(n => n.Pitch), turn.OcarinaMelody!.Select(n => n.Pitch));
        Assert.Equal(s.OcarinaMelody!.Select(n => n.Figure.ToString()), turn.OcarinaMelody!.Select(n => n.Figure));
        Assert.InRange(turn.OcarinaListenRemainingMs, OcarinaMelody.PlaybackMs(s.OcarinaMelody!) - 1000, OcarinaMelody.PlaybackMs(s.OcarinaMelody!));
    }

    [Fact]
    public async Task Attempt_WhileMelodyIsPlaying_Fails()
    {
        var roomCode = await StartRoomAsync(skipListening: false);

        var result = await PlayAsync(roomCode, 200L, await MelodyAsync(roomCode));

        Assert.True(result.IsFailure);
        Assert.True((await SessionAsync(roomCode)).OcarinaOpen);
    }

    [Fact]
    public async Task WrongAttempt_ReturnsFalse_KeepsOpen_AndCanRetry()
    {
        var roomCode = await StartRoomAsync();
        var melody = await MelodyAsync(roomCode);

        var fallo = await PlayAsync(roomCode, 200L, Wrong(melody));
        Assert.True(fallo.IsSuccess);
        Assert.False(fallo.Value);
        Assert.True((await SessionAsync(roomCode)).OcarinaOpen);
        Assert.DoesNotContain(_sent, m => m.Method == "OcarinaWon");

        var acierto = await PlayAsync(roomCode, 200L, melody);
        Assert.True(acierto.Value);
    }

    [Fact]
    public async Task FirstCorrect_WinsAndAnswers_OthersFindItClosed()
    {
        var roomCode = await StartRoomAsync();
        var melody = await MelodyAsync(roomCode);

        var primero = await PlayAsync(roomCode, 300L, melody);
        var segundo = await PlayAsync(roomCode, 200L, melody);

        Assert.True(primero.Value);
        Assert.True(segundo.IsFailure);
        var s = await SessionAsync(roomCode);
        Assert.False(s.OcarinaOpen);
        Assert.Equal(300L, s.GetCurrentPlayerId());

        var won = Assert.IsType<OcarinaWonDto>(_sent.Single(m => m.Method == "OcarinaWon").Payload);
        Assert.Equal(300L, won.PlayerId);
        var turn = Assert.IsType<TurnStartedDto>(_sent.Last(m => m.Method == "TurnStarted").Payload);
        Assert.Equal(300L, turn.CurrentPlayerId);
        Assert.False(turn.OcarinaOpen);
        Assert.Null(turn.OcarinaMelody);
        // Con la ocarina resuelta vuelve el tiempo de turno de la sala.
        Assert.True(turn.TimeLimit > 0);
    }

    [Fact]
    public async Task Winner_AnswersAndQueueDoesNotAdvance()
    {
        var roomCode = await StartRoomAsync();
        var cola = (await SessionAsync(roomCode)).TurnQueue.ToList();
        await PlayAsync(roomCode, 200L, await MelodyAsync(roomCode));

        var s = await SessionAsync(roomCode);
        var turn = await _service.SubmitAnswerAsync(roomCode, 200L, s.Questions[0].Id, s.Questions[0].Respuestas.FindIndex(r => r.EsCorrecta));
        Assert.True(turn!.IsCorrect);

        s = await SessionAsync(roomCode);
        if (s.State == GameState.PhaseBreak) await _service.ContinuePhaseAsync(roomCode, Owner);
        s = await SessionAsync(roomCode);
        Assert.Equal(cola[0], s.GetCurrentPlayerId());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    public async Task InvalidNote_Fails(int nota)
    {
        var roomCode = await StartRoomAsync();

        Assert.True((await PlayAsync(roomCode, 200L, [0, 1, 2, 3, 4, nota])).IsFailure);
    }

    [Fact]
    public async Task OwnerCannotPlay_AndNoComodines()
    {
        var roomCode = await StartRoomAsync();
        var s = await SessionAsync(roomCode);

        Assert.True((await PlayAsync(roomCode, Owner, await MelodyAsync(roomCode))).IsFailure);
        Assert.True((await _service.UseComodinAsync(roomCode, 200L, ComodinTipo.Robo, s.Questions[0].Id)).IsFailure);
    }

    [Fact]
    public async Task HostCanSkipOcarinaQuestion()
    {
        var roomCode = await StartRoomAsync();

        Assert.True((await _service.NextQuestionAsync(roomCode, Owner)).IsSuccess);

        var s = await SessionAsync(roomCode);
        Assert.Equal(1, s.CurrentQuestionIndex);
        Assert.False(s.OcarinaOpen);
    }

    [Fact]
    public async Task Kick_Winner_ReopensOcarinaWithoutListeningAgain()
    {
        var roomCode = await StartRoomAsync();
        var melody = await MelodyAsync(roomCode);
        await PlayAsync(roomCode, 200L, melody);

        await _service.KickPlayerAsync(roomCode, Owner, 200L);

        var s = await SessionAsync(roomCode);
        Assert.True(s.OcarinaOpen);
        Assert.False(s.BuzzerOpen);
        Assert.Null(s.GetCurrentPlayerId());
        Assert.True((await PlayAsync(roomCode, 300L, melody)).Value);
    }

    [Fact]
    public async Task EachOcarinaQuestion_GetsItsOwnMelody()
    {
        _questions = Questions(TiposPregunta.Ocarina, TiposPregunta.Ocarina, TiposPregunta.Ocarina, TiposPregunta.Ocarina);
        var roomCode = await StartRoomAsync();
        var melodies = new HashSet<string>();

        for (var i = 0; i < 4; i++)
        {
            melodies.Add(string.Join(",", await MelodyAsync(roomCode)));
            await _service.NextQuestionAsync(roomCode, Owner);
            if ((await SessionAsync(roomCode)).State == GameState.PhaseBreak) await _service.ContinuePhaseAsync(roomCode, Owner);
        }

        Assert.True(melodies.Count > 1);
    }
}
