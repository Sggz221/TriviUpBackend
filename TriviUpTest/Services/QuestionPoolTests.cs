using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Errors;
using TriviUpBackend.Game.Configuration;
using TriviUpBackend.Game.Hubs;
using TriviUpBackend.Game.Persistence;
using TriviUpBackend.Game.Services;
using TriviUpBackend.Services.Cache;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;

namespace TriviUpTest.Services;

/// <summary>Pool de preguntas: fases cuyas preguntas se sortean del banco del autor en cada partida.</summary>
public class QuestionPoolTests
{
    private const long Autor = 1;

    // ---------- banco simulado ----------

    private readonly List<BancoPregunta> _banco = [];
    private readonly Mock<IBancoPreguntaRepository> _bancoRepo = new();

    public QuestionPoolTests()
    {
        _bancoRepo.Setup(r => r.FindIdsAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string?>()))
            .ReturnsAsync((long creator, long? categoria, string? dificultad) => _banco
                .Where(p => p.CreatorId == creator &&
                            (categoria == null || p.CategoriaId == categoria) &&
                            (dificultad == null || p.Dificultad == dificultad))
                .Select(p => p.Id).ToList());
        _bancoRepo.Setup(r => r.FindByIdsAsync(It.IsAny<long>(), It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync((long creator, IReadOnlyCollection<long> ids) =>
                _banco.Where(p => p.CreatorId == creator && ids.Contains(p.Id)).ToList());
    }

    private BancoPregunta Banco(long id, long? categoria = null, string? dificultad = null, long creator = Autor)
    {
        var p = new BancoPregunta
        {
            Id = id, CreatorId = creator, Enunciado = $"Banco {id}", CategoriaId = categoria, Dificultad = dificultad,
            Respuestas = [new BancoRespuesta { Texto = "Sí", EsCorrecta = true }, new BancoRespuesta { Texto = "No" }]
        };
        _banco.Add(p);
        return p;
    }

    private static FasePool Filtros(int fase, int cantidad, long? categoria = null, string? dificultad = null) => new()
    {
        FaseNumero = fase, Cantidad = cantidad, Origen = OrigenesPool.Filtros, CategoriaId = categoria, Dificultad = dificultad
    };

    private static FasePool Manual(int fase, int cantidad, params long[] ids) => new()
    {
        FaseNumero = fase, Cantidad = cantidad, Origen = OrigenesPool.Manual,
        Preguntas = ids.Select(id => new FasePoolPregunta { Id = id, Enunciado = $"Banco {id}" }).ToList()
    };

    private static Quiz QuizCon(params FasePool[] pools) => new() { Id = 7, CreatorId = Autor, Nombre = "Q", Pools = pools.ToList() };

    // ---------- sorteo ----------

    [Fact]
    public async Task Draw_Filters_OnlyMatchingQuestionsFromAuthorsBank()
    {
        Banco(1, categoria: 10, dificultad: "facil");
        Banco(2, categoria: 10, dificultad: "dificil");
        Banco(3, categoria: 20, dificultad: "facil");
        Banco(4, categoria: 10, dificultad: "facil", creator: 99);

        var preguntas = await PoolDrawer.DrawAsync(QuizCon(Filtros(1, 10, categoria: 10, dificultad: "facil")), _bancoRepo.Object, new Random(1));

        Assert.Equal([-1L], preguntas.Select(p => p.Id));
    }

    [Fact]
    public async Task Draw_TakesExactlyTheRequestedAmount()
    {
        for (var i = 1; i <= 10; i++) Banco(i);

        var preguntas = await PoolDrawer.DrawAsync(QuizCon(Filtros(1, 4)), _bancoRepo.Object, new Random(3));

        Assert.Equal(4, preguntas.Count);
        Assert.Equal(4, preguntas.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public async Task Draw_NotEnoughQuestions_PlaysWhatThereIs()
    {
        Banco(1);
        Banco(2);

        var preguntas = await PoolDrawer.DrawAsync(QuizCon(Filtros(1, 5)), _bancoRepo.Object, new Random(1));

        Assert.Equal(2, preguntas.Count);
    }

    [Fact]
    public async Task Draw_Manual_OnlyPicksChosenQuestions_AndSkipsDeletedOrForeignOnes()
    {
        Banco(1);
        Banco(2);
        Banco(3);
        Banco(4, creator: 99);

        // 2 elegidas que existen (1 y 3), una borrada (50) y una de otro usuario (4)
        var preguntas = await PoolDrawer.DrawAsync(QuizCon(Manual(1, 4, 1, 3, 50, 4)), _bancoRepo.Object, new Random(1));

        Assert.Equal([1L, 3L], preguntas.Select(p => -p.Id).Order());
    }

    [Fact]
    public async Task Draw_SameBankQuestionNeverAppearsTwiceInAGame()
    {
        Banco(1);
        Banco(2);

        var preguntas = await PoolDrawer.DrawAsync(QuizCon(Manual(1, 2, 1, 2), Filtros(2, 2)), _bancoRepo.Object, new Random(1));

        Assert.Equal(2, preguntas.Count);
        Assert.Equal(2, preguntas.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public async Task Draw_CarriesPhaseDataAndSkipsUnplayableQuestions()
    {
        Banco(1);
        var rota = Banco(2);
        rota.Respuestas = [new BancoRespuesta { Texto = "Sí", EsCorrecta = true }, new BancoRespuesta { Texto = "No", EsCorrecta = true }];

        var pool = Filtros(2, 5) with { FaseNombre = "Ronda banco", FaseColor = "#ff8800" };
        var preguntas = await PoolDrawer.DrawAsync(QuizCon(pool), _bancoRepo.Object, new Random(1));

        var p = Assert.Single(preguntas);
        Assert.Equal((2, "Ronda banco", "#ff8800", TiposPregunta.Normal), (p.FaseNumero, p.FaseNombre, p.FaseColor, p.Tipo));
        Assert.Single(p.Respuestas, r => r.EsCorrecta);
    }

    // ---------- validación y guardado ----------

    private static QuizService NewQuizService(Mock<IQuizRepository> repo) =>
        new(repo.Object, new Mock<ILogger<QuizService>>().Object, new Mock<ICacheService>().Object);

    private static Mock<IQuizRepository> QuizRepo(Action<Quiz>? onSave = null)
    {
        var repo = new Mock<IQuizRepository>();
        Quiz? saved = null;
        repo.Setup(r => r.FindByGameCodeAsync(It.IsAny<string>())).ReturnsAsync((Quiz?)null);
        repo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).Callback<Quiz>(q => { saved = q; onSave?.Invoke(q); }).ReturnsAsync((Quiz q) => q);
        repo.Setup(r => r.FindByIdWithQuestionsAsync(It.IsAny<long>())).ReturnsAsync(() => saved);
        return repo;
    }

    private static CreatePreguntaRequest Q(int numero, int fase) => new()
    {
        NumeroPregunta = numero, Enunciado = $"P{numero}", FaseNumero = fase,
        Respuestas = [new CreateRespuestaRequest { Texto = "A", EsCorrecta = true }, new CreateRespuestaRequest { Texto = "B" }]
    };

    private static CreateQuizRequest Request(IEnumerable<CreatePreguntaRequest> preguntas, params FasePool[] pools) =>
        new() { Nombre = "Quiz", Preguntas = preguntas.ToList(), Pools = pools.ToList() };

    [Fact]
    public async Task Create_PoolBetweenQuestionPhases_IsSavedAndReturned()
    {
        Quiz? saved = null;
        var service = NewQuizService(QuizRepo(q => saved = q));

        var result = await service.CreateAsync(Request([Q(1, 1), Q(2, 3)], Filtros(2, 3, dificultad: " Media ")), Autor);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        var pool = Assert.Single(saved!.Pools);
        Assert.Equal((2, 3, "media"), (pool.FaseNumero, pool.Cantidad, pool.Dificultad));
        Assert.Equal(2, Assert.Single(result.Value.Pools).FaseNumero);
    }

    [Fact]
    public async Task Create_OnlyPools_IsValid()
    {
        var result = await NewQuizService(QuizRepo()).CreateAsync(Request([], Filtros(1, 5)), Autor);

        Assert.True(result.IsSuccess);
    }

    public static TheoryData<FasePool[], CreatePreguntaRequest[]> PoolsInvalidos => new()
    {
        { [Filtros(2, 3)], [Q(1, 1), Q(2, 2)] },          // pool y preguntas en la misma fase
        { [Filtros(3, 3)], [Q(1, 1)] },                   // hueco: falta la fase 2
        { [Filtros(1, 3), Filtros(1, 2)], [] },           // dos pools en la misma fase
        { [Filtros(1, 0)], [] },                          // no saca ninguna
        { [Filtros(1, 3, dificultad: "imposible")], [] }, // dificultad desconocida
        { [Manual(1, 3)], [] },                           // manual sin preguntas elegidas
        { [Manual(1, 3, 1, 2)], [] },                     // pide más de las elegidas
        { [Filtros(1, 3) with { Origen = "otro" }], [] }  // origen desconocido
    };

    [Theory]
    [MemberData(nameof(PoolsInvalidos))]
    public async Task Create_InvalidPool_ReturnsValidationErrorWhenPublishing(FasePool[] pools, CreatePreguntaRequest[] preguntas)
    {
        var result = await NewQuizService(QuizRepo()).CreateAsync(Request(preguntas, pools), Autor);

        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task Create_DraftWithIncompletePool_IsSavedNormalized()
    {
        Quiz? saved = null;
        var service = NewQuizService(QuizRepo(q => saved = q));

        var request = Request([], Manual(1, 0, 5, 5) with { CategoriaId = 3 }) with { EsBorrador = true };
        var result = await service.CreateAsync(request, Autor);

        Assert.True(result.IsSuccess);
        var pool = Assert.Single(saved!.Pools);
        Assert.Equal(1, pool.Cantidad);
        Assert.Single(pool.Preguntas);
        Assert.Null(pool.CategoriaId); // solo aplica a los pools por filtros
    }

    // ---------- partida ----------

    [Fact]
    public async Task StartGame_PoolQuestionsAreDrawnIntoTheirPhase_AndEmptyPoolsAreSkipped()
    {
        Banco(1);
        Banco(2);
        Banco(3);
        var quiz = QuizCon(Filtros(2, 2), Manual(3, 1, 999)); // la fase 3 se queda sin preguntas (999 no existe)
        List<Pregunta> fijas =
        [
            new() { Id = 100, NumeroPregunta = 1, Enunciado = "Fija", FaseNumero = 1,
                    Respuestas = [new Respuesta { Id = 1001, Texto = "Sí", EsCorrecta = true }, new Respuesta { Id = 1002, Texto = "No" }] }
        ];

        var store = new InMemoryGameSessionStore();
        var service = NewGameService(store, quiz, fijas);
        var roomCode = await service.CreateGameAsync(quiz.Id, 100L, "owner", "conn-owner");
        await service.JoinGameAsync(roomCode, 200L, "p1", "conn-200");
        await service.JoinGameAsync(roomCode, 300L, "p2", "conn-300");

        Assert.NotNull(await service.StartGameAsync(roomCode, 100L));

        var session = (await store.GetAsync(roomCode))!;
        // 3 preguntas y 2 jugadores: se descarta la última para que ambos respondan lo mismo.
        Assert.Equal([1, 2], session.Questions.Select(q => q.FaseNumero));
        Assert.Equal(100L, session.Questions[0].Id);
        Assert.All(session.Questions.Skip(1), q => Assert.True(q.Id < 0));
    }

    private GameService NewGameService(InMemoryGameSessionStore store, Quiz quiz, List<Pregunta> fijas)
    {
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);
        hubClients.Setup(c => c.Client(It.IsAny<string>())).Returns(new Mock<ISingleClientProxy>().Object);
        var hubContext = new Mock<IHubContext<GameHub>>();
        hubContext.Setup(h => h.Clients).Returns(hubClients.Object);

        var quizRepository = new Mock<IQuizRepository>();
        quizRepository.Setup(r => r.FindByIdAsync(It.IsAny<long>())).ReturnsAsync(quiz);
        quizRepository.Setup(r => r.GetQuestionsWithAnswersAsync(It.IsAny<long>())).ReturnsAsync(() => fijas.ToList());

        var provider = new Mock<IServiceProvider>();
        provider.Setup(sp => sp.GetService(typeof(IHubContext<GameHub>))).Returns(hubContext.Object);
        provider.Setup(sp => sp.GetService(typeof(IQuizRepository))).Returns(quizRepository.Object);
        provider.Setup(sp => sp.GetService(typeof(IBancoPreguntaRepository))).Returns(_bancoRepo.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var options = new GameOptions { MaxPlayersPerRoom = 10, MinPlayersToStart = 2, QuestionTimeLimit = 21, RoomLockTimeoutMs = 2000 };
        return new GameService(store, options, new Mock<ILogger<GameService>>().Object, scopeFactory.Object);
    }
}
