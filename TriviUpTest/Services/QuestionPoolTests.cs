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
using TriviUpBackend.Game.Models;
using TriviUpBackend.Game.Persistence;
using TriviUpBackend.Game.Services;
using TriviUpBackend.Services.Cache;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;

namespace TriviUpTest.Services;

/// <summary>Pool de preguntas: fases cuyas preguntas salen, de una en una, del banco de quien crea la sala.</summary>
public class QuestionPoolTests
{
    private const long Autor = 1;

    // ---------- banco simulado ----------

    private readonly List<BancoPregunta> _banco = [];
    private readonly Mock<IBancoPreguntaRepository> _bancoRepo = new();

    public QuestionPoolTests()
    {
        _bancoRepo.Setup(r => r.FindIdsAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync((long creator, long? categoria, string? dificultad, string tipo) => _banco
                .Where(p => p.CreatorId == creator && p.Tipo == tipo &&
                            (categoria == null || p.CategoriaId == categoria) &&
                            (dificultad == null || p.Dificultad == dificultad))
                .Select(p => p.Id).ToList());
        _bancoRepo.Setup(r => r.FindByIdsAsync(It.IsAny<long>(), It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync((long creator, IReadOnlyCollection<long> ids) =>
                _banco.Where(p => p.CreatorId == creator && ids.Contains(p.Id)).ToList());
    }

    private BancoPregunta Banco(
        long id, long? categoria = null, string? dificultad = null, long creator = Autor, string tipo = TiposPregunta.Normal)
    {
        var p = new BancoPregunta
        {
            Id = id, CreatorId = creator, Enunciado = $"Banco {id}", CategoriaId = categoria, Dificultad = dificultad, Tipo = tipo,
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

    /// <summary>Pool que, de sus preguntas, saca estas de cada tipo especial.</summary>
    private static FasePool ConEspeciales(FasePool pool, int pulsador = 0, int colores = 0, int ocarina = 0) =>
        pool with { Pulsador = pulsador, Colores = colores, Ocarina = ocarina };

    private static Quiz QuizCon(params FasePool[] pools) => new() { Id = 7, CreatorId = Autor, Nombre = "Q", Pools = pools.ToList() };

    // ---------- sorteo (de una en una, del banco de quien crea la sala) ----------

    private static PoolSpecDocument Spec(FasePool pool, bool bancoDelAutor = true) => BankDrawer.ToSpec(pool, bancoDelAutor);

    /// <summary>Saca preguntas hasta vaciar lo que permite el pool, como haría una partida larga.</summary>
    private async Task<List<long>> DrawAllAsync(PoolSpecDocument spec, long owner = Autor)
    {
        var used = new List<long>();
        while (await BankDrawer.DrawAsync(_bancoRepo.Object, owner, spec, used, new Random(used.Count)) is { } p) used.Add(p.Id);
        return used;
    }

    [Fact]
    public async Task Draw_Filters_OnlyMatchingQuestionsFromTheBank()
    {
        Banco(1, categoria: 10, dificultad: "facil");
        Banco(2, categoria: 10, dificultad: "dificil");
        Banco(3, categoria: 20, dificultad: "facil");
        Banco(4, categoria: 10, dificultad: "facil", creator: 99);

        Assert.Equal([1L], await DrawAllAsync(Spec(Filtros(1, 10, categoria: 10, dificultad: "facil"))));
    }

    [Fact]
    public async Task Draw_ByType_OnlyQuestionsOfThatType_AndNormalDrawsNeverTakeSpecials()
    {
        Banco(1);
        Banco(2, tipo: TiposPregunta.Pulsador);
        Banco(3, tipo: TiposPregunta.Ocarina);
        var spec = Spec(Filtros(1, 10));

        Assert.Equal([1L], await DrawAllAsync(spec));
        var used = new List<long>();
        Assert.Equal(2L, (await BankDrawer.DrawAsync(_bancoRepo.Object, Autor, spec, used, new Random(1), TiposPregunta.Pulsador))!.Id);
        Assert.Null(await BankDrawer.DrawAsync(_bancoRepo.Object, Autor, spec, used, new Random(1), TiposPregunta.Colores));
    }

    [Fact]
    public void Normalizar_ClampsSpecialCountsToTheTotal()
    {
        var pool = OrigenesPool.Normalizar(ConEspeciales(Filtros(1, 3), pulsador: 2, colores: 5, ocarina: -1));

        Assert.Equal((3, 2, 1, 0, 0), (pool.Cantidad, pool.Pulsador, pool.Colores, pool.Ocarina, pool.Normales));
    }

    [Fact]
    public async Task Draw_NeverRepeatsAQuestionAlreadyUsedInTheGame()
    {
        for (var i = 1; i <= 6; i++) Banco(i);

        var ids = await DrawAllAsync(Spec(Filtros(1, 2)));

        // El pool pide 2 por partida, pero el banco entero sirve: salen todas, sin repetir
        Assert.Equal([1L, 2, 3, 4, 5, 6], ids.Order());
    }

    [Fact]
    public async Task Draw_Manual_OnlyPicksChosenQuestions_AndSkipsDeletedOrForeignOnes()
    {
        Banco(1);
        Banco(2);
        Banco(3);
        Banco(4, creator: 99);

        // 2 elegidas que existen (1 y 3), una borrada (50) y una de otro usuario (4)
        Assert.Equal([1L, 3L], (await DrawAllAsync(Spec(Manual(1, 4, 1, 3, 50, 4)))).Order());
    }

    [Fact]
    public async Task Draw_SkipsUnplayableQuestions()
    {
        Banco(1);
        var rota = Banco(2);
        rota.Respuestas = [new BancoRespuesta { Texto = "Sí", EsCorrecta = true }, new BancoRespuesta { Texto = "No", EsCorrecta = true }];

        Assert.Equal([1L], await DrawAllAsync(Spec(Filtros(1, 5))));
    }

    [Fact]
    public void Spec_RoomCreatedByAnotherUser_UsesTheirWholeBankWithThePoolDifficulty()
    {
        var manual = Spec(Manual(1, 2, 1, 2), bancoDelAutor: false);
        var filtros = Spec(Filtros(2, 3, categoria: 10, dificultad: "media"), bancoDelAutor: false);

        // Las preguntas y categorías elegidas son del banco del autor: no valen en el de otro
        Assert.Null(manual.CandidateIds);
        Assert.Null(filtros.CategoriaId);
        Assert.Equal("media", filtros.Dificultad);
    }

    [Fact]
    public async Task Fill_KeepsThePhaseOfTheSlot_AndTheBankData()
    {
        var banco = Banco(1);
        banco.Curiosidad = "Dato curioso";
        var spec = Spec(Filtros(2, 5) with { FaseNombre = "Ronda banco", FaseColor = "#ff8800" });

        var drawn = (await BankDrawer.DrawAsync(_bancoRepo.Object, Autor, spec, [], new Random(1)))!;
        var q = BankDrawer.Fill(BankDrawer.Slot(spec, 0), drawn, new Random(1));

        Assert.Equal((-1L, 2, "Ronda banco", "#ff8800", 0), (q.Id, q.FaseNumero, q.FaseNombre, q.FaseColor, q.PoolIndex));
        Assert.Equal("Dato curioso", q.Curiosidad);
        Assert.False(q.PendingDraw);
        Assert.Single(q.Respuestas, r => r.EsCorrecta);
    }

    [Fact]
    public void Fill_PlaysTheQuestionAsItsBankType()
    {
        var banco = Banco(1, tipo: TiposPregunta.Colores);
        var spec = Spec(ConEspeciales(Filtros(1, 1), colores: 1));

        var q = BankDrawer.Fill(BankDrawer.Slot(spec, 0, TiposPregunta.Colores), banco, new Random(1));

        Assert.True(q.EsColores);
        Assert.True(q.SinTurno);
        Assert.Equal(TiposPregunta.Colores, q.Tipo);
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
        { [Filtros(1, 3) with { Origen = "otro" }], [] }, // origen desconocido
        { [ConEspeciales(Filtros(1, 2), pulsador: 2, ocarina: 1)], [] }, // más especiales que preguntas
        { [ConEspeciales(Filtros(1, 2), colores: -1)], [] },           // cantidad negativa
        { [ConEspeciales(Manual(1, 2, 1, 2), pulsador: 1)], [] }       // ninguna elegida es de pulsador
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
    public async Task Create_ManualPoolWithSpecials_TakesTheTypesFromTheBank()
    {
        Banco(1);
        Banco(2, tipo: TiposPregunta.Pulsador);
        Banco(3, tipo: TiposPregunta.Ocarina);
        Quiz? saved = null;
        var service = new QuizService(QuizRepo(q => saved = q).Object, new Mock<ILogger<QuizService>>().Object,
            new Mock<ICacheService>().Object, _bancoRepo.Object);

        var result = await service.CreateAsync(Request([], ConEspeciales(Manual(1, 3, 1, 2, 3), pulsador: 1, ocarina: 1)), Autor);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        var pool = Assert.Single(saved!.Pools);
        Assert.Equal((1, 1, 1), (pool.Normales, pool.Pulsador, pool.Ocarina));
        Assert.Equal([TiposPregunta.Normal, TiposPregunta.Pulsador, TiposPregunta.Ocarina], pool.Preguntas.Select(p => p.Tipo));
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

    private static Pregunta Fija(long id, int fase = 1) => new()
    {
        Id = id, NumeroPregunta = (int)id, Enunciado = $"Fija {id}", FaseNumero = fase,
        Respuestas = [new Respuesta { Id = id * 10 + 1, Texto = "Sí", EsCorrecta = true }, new Respuesta { Id = id * 10 + 2, Texto = "No" }]
    };

    /// <summary>Sala empezada con el anfitrión <paramref name="owner"/> y <paramref name="players"/> jugadores.</summary>
    private async Task<(GameService Service, InMemoryGameSessionStore Store, string Room)> StartAsync(
        Quiz quiz, List<Pregunta> fijas, long owner = Autor, int players = 1)
    {
        var store = new InMemoryGameSessionStore();
        var service = NewGameService(store, quiz, fijas);
        var roomCode = await service.CreateGameAsync(quiz.Id, owner, "owner", "conn-owner");
        for (var i = 0; i < players; i++) await service.JoinGameAsync(roomCode, 200L + i, $"p{i}", $"conn-{i}");
        Assert.NotNull(await service.StartGameAsync(roomCode, owner));
        return (service, store, roomCode);
    }

    private static QuestionSnapshot Current(GameSessionDocument s) => s.Questions[s.CurrentQuestionIndex];

    private static async Task AnswerAsync(GameService service, InMemoryGameSessionStore store, string room)
    {
        var s = (await store.GetAsync(room))!;
        await service.SubmitAnswerAsync(room, s.GetCurrentPlayerId()!.Value, Current(s).Id, Current(s).Respuestas.FindIndex(r => r.EsCorrecta));
    }

    [Fact]
    public async Task StartGame_PoolPhasesGetSlots_NothingIsDrawnUpFront_AndEmptyPoolsAreSkipped()
    {
        Banco(1);
        Banco(2);
        Banco(3);
        var quiz = QuizCon(Filtros(2, 2), Manual(3, 1, 999)); // la fase 3 se queda sin preguntas (999 no existe)

        var (_, store, room) = await StartAsync(quiz, [Fija(100)], players: 2);

        var session = (await store.GetAsync(room))!;
        // 3 preguntas y 2 jugadores: se descarta la última para que ambos respondan lo mismo.
        Assert.Equal([1, 2], session.Questions.Select(q => q.FaseNumero));
        Assert.Equal(100L, session.Questions[0].Id);
        Assert.True(session.Questions[1].PendingDraw);
        Assert.Empty(session.UsedBankIds);
        _bancoRepo.Verify(r => r.FindByIdsAsync(It.IsAny<long>(), It.IsAny<IReadOnlyCollection<long>>()), Times.Never);
    }

    [Fact]
    public async Task StartGame_ReservesSlotsPerType_AndSpecialSlotsAreNeverTrimmed()
    {
        for (var i = 1; i <= 3; i++) Banco(i);
        Banco(4, tipo: TiposPregunta.Pulsador);
        Banco(5, tipo: TiposPregunta.Ocarina);
        // Pide 2 pulsadores pero el banco solo tiene 1: sale 1
        var quiz = QuizCon(ConEspeciales(Filtros(1, 6), pulsador: 2, ocarina: 1));

        var (_, store, room) = await StartAsync(quiz, [], players: 2);

        var session = (await store.GetAsync(room))!;
        // 3 normales y 2 jugadores: se recorta una normal; las especiales (sin turno) se quedan
        Assert.Equal(2, session.Questions.Count(q => !q.SinTurno));
        Assert.Equal([TiposPregunta.Ocarina, TiposPregunta.Pulsador],
            session.Questions.Where(q => q.SinTurno).Select(q => q.Tipo).Order());
    }

    [Fact]
    public async Task SpecialPoolSlot_IsDrawnFromTheBankQuestionsOfItsType_AndPlayedAsSuch()
    {
        Banco(1);
        Banco(2, tipo: TiposPregunta.Pulsador);
        var (_, store, room) = await StartAsync(QuizCon(ConEspeciales(Filtros(1, 1), pulsador: 1)), []);

        var current = Current((await store.GetAsync(room))!);

        Assert.Equal(-2L, current.Id);
        Assert.True(current.EsPulsador);
        Assert.False(current.PendingDraw);
    }

    [Fact]
    public async Task PoolSlot_IsDrawnFromTheBankWhenItsTurnComes()
    {
        Banco(1);
        Banco(2);
        var (service, store, room) = await StartAsync(QuizCon(Filtros(2, 1)), [Fija(100)]);

        await AnswerAsync(service, store, room);

        var session = (await store.GetAsync(room))!;
        var drawn = Current(session);
        Assert.Equal(1, session.CurrentQuestionIndex);
        Assert.False(drawn.PendingDraw);
        Assert.True(drawn.Id < 0);
        Assert.Equal([-drawn.Id], session.UsedBankIds);
    }

    [Fact]
    public async Task PoolQuestions_NeverRepeatInAGame()
    {
        for (var i = 1; i <= 4; i++) Banco(i);
        var (service, store, room) = await StartAsync(QuizCon(Filtros(1, 4)), []);

        var seen = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            seen.Add(Current((await store.GetAsync(room))!).Id);
            await AnswerAsync(service, store, room);
        }

        Assert.Equal([-4L, -3, -2, -1], seen.Order());
    }

    [Fact]
    public async Task PoolQuestions_ComeFromTheBankOfWhoCreatesTheRoom()
    {
        Banco(1);                // del autor del cuestionario
        Banco(2, creator: 500);  // de quien crea la sala
        var quiz = QuizCon(Manual(1, 1, 1));

        var (_, store, room) = await StartAsync(quiz, [], owner: 500);

        Assert.Equal(-2L, Current((await store.GetAsync(room))!).Id);
    }

    [Fact]
    public async Task CambiarPregunta_InAPoolPhase_DrawsANewBankQuestion_AndTheOldOneNeverComesBack()
    {
        for (var i = 1; i <= 3; i++) Banco(i);
        var (service, store, room) = await StartAsync(QuizCon(Filtros(1, 2)), []);
        var s = (await store.GetAsync(room))!;
        var old = Current(s).Id;

        var result = await service.UseComodinAsync(room, s.GetCurrentPlayerId()!.Value, ComodinTipo.CambiarPregunta, old);

        Assert.True(result.IsSuccess);
        var after = (await store.GetAsync(room))!;
        var nueva = Current(after).Id;
        Assert.NotEqual(old, nueva);
        Assert.Equal(2, after.Questions.Count);
        Assert.Equal(2, after.UsedBankIds.Count);

        // La siguiente es la única del banco que no ha salido: la cambiada no vuelve
        await AnswerAsync(service, store, room);
        var last = Current((await store.GetAsync(room))!).Id;
        Assert.DoesNotContain(last, new[] { old, nueva });
    }

    [Fact]
    public async Task CambiarPregunta_NeverSwapsInASpecialQuestion()
    {
        Banco(1);
        Banco(2);
        Banco(3, tipo: TiposPregunta.Pulsador);
        var (service, store, room) = await StartAsync(QuizCon(Filtros(1, 1)), []);
        var s = (await store.GetAsync(room))!;

        var result = await service.UseComodinAsync(room, s.GetCurrentPlayerId()!.Value, ComodinTipo.CambiarPregunta, Current(s).Id);

        Assert.True(result.IsSuccess);
        var nueva = Current((await store.GetAsync(room))!);
        Assert.NotEqual(-3L, nueva.Id);
        Assert.False(nueva.SinTurno);
    }

    [Fact]
    public async Task CambiarPregunta_InAPoolPhase_BankExhausted_FailsWithoutSpendingTheComodin()
    {
        Banco(1);
        var (service, store, room) = await StartAsync(QuizCon(Filtros(1, 1)), []);
        var s = (await store.GetAsync(room))!;
        var player = s.GetCurrentPlayerId()!.Value;

        var result = await service.UseComodinAsync(room, player, ComodinTipo.CambiarPregunta, Current(s).Id);

        Assert.True(result.IsFailure);
        Assert.Empty((await store.GetAsync(room))!.Players.Single(p => p.UserId == player).UsedComodines);
    }

    [Fact]
    public async Task CambiarPregunta_InAFixedPhase_TakesOneFromTheBank_AndDropsTheOldOne()
    {
        Banco(1);
        var (service, store, room) = await StartAsync(QuizCon(), [Fija(100), Fija(101)]);
        var s = (await store.GetAsync(room))!;
        var old = Current(s).Id;

        var result = await service.UseComodinAsync(room, s.GetCurrentPlayerId()!.Value, ComodinTipo.CambiarPregunta, old);

        Assert.True(result.IsSuccess);
        var after = (await store.GetAsync(room))!;
        Assert.Equal(-1L, Current(after).Id);
        Assert.Equal(2, after.Questions.Count);
        Assert.DoesNotContain(after.Questions, q => q.Id == old);
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
