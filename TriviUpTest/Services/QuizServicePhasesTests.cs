using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Errors;
using TriviUpBackend.Services.Cache;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;

namespace TriviUpTest.Services;

/// <summary>Fases de un cuestionario: validación y persistencia en borradores/versiones.</summary>
public class QuizServicePhasesTests
{
    private readonly Mock<IQuizRepository> _repo = new();
    private readonly QuizService _service;

    public QuizServicePhasesTests()
    {
        _service = new QuizService(_repo.Object, new Mock<ILogger<QuizService>>().Object, new Mock<ICacheService>().Object);
        _repo.Setup(r => r.FindByGameCodeAsync(It.IsAny<string>())).ReturnsAsync((Quiz?)null);
        _repo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).ReturnsAsync((Quiz q) => q);
        _repo.Setup(r => r.FindByIdWithQuestionsAsync(It.IsAny<long>())).ReturnsAsync((long _) => null);
    }

    private static CreatePreguntaRequest CreateQ(int numero, int fase, string? nombre, string? color = null) => new()
    {
        NumeroPregunta = numero,
        Enunciado = $"Pregunta {numero}",
        FaseNumero = fase,
        FaseNombre = nombre,
        FaseColor = color,
        Respuestas =
        [
            new CreateRespuestaRequest { Texto = "A", EsCorrecta = true },
            new CreateRespuestaRequest { Texto = "B", EsCorrecta = false }
        ]
    };

    private static CreateQuizRequest Request(params CreatePreguntaRequest[] preguntas) =>
        new() { Nombre = "Quiz", Preguntas = preguntas.ToList() };

    [Fact]
    public async Task CreateAsync_TwoConsecutivePhases_SavesPhaseFieldsOnQuestions()
    {
        Quiz? saved = null;
        _repo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).Callback<Quiz>(q => saved = q).ReturnsAsync((Quiz q) => q);
        _repo.Setup(r => r.FindByIdWithQuestionsAsync(It.IsAny<long>())).ReturnsAsync(() => saved);

        var result = await _service.CreateAsync(
            Request(CreateQ(1, 1, "Ronda 1"), CreateQ(2, 1, " Ronda 1 "), CreateQ(3, 2, "  ")), creatorId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal([1, 1, 2], saved!.Preguntas.Select(p => p.FaseNumero));
        Assert.Equal(["Ronda 1", "Ronda 1", null], saved.Preguntas.Select(p => p.FaseNombre));
        Assert.Equal(2, result.Value.Preguntas[2].FaseNumero);
    }

    [Fact]
    public async Task CreateAsync_PhasesNotStartingAtOne_ReturnsValidationError()
    {
        var result = await _service.CreateAsync(Request(CreateQ(1, 2, null), CreateQ(2, 2, null)), creatorId: 1);

        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_EmptyPhaseBetweenOthers_ReturnsValidationError()
    {
        var result = await _service.CreateAsync(Request(CreateQ(1, 1, null), CreateQ(2, 3, null)), creatorId: 1);

        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_PhaseGoingBackwards_ReturnsValidationError()
    {
        var result = await _service.CreateAsync(
            Request(CreateQ(1, 1, null), CreateQ(2, 2, null), CreateQ(3, 1, null)), creatorId: 1);

        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_SamePhaseWithDifferentNames_ReturnsValidationError()
    {
        var result = await _service.CreateAsync(
            Request(CreateQ(1, 1, "Ronda 1"), CreateQ(2, 1, "Otra")), creatorId: 1);

        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_PhaseColor_IsStoredNormalized()
    {
        Quiz? saved = null;
        _repo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).Callback<Quiz>(q => saved = q).ReturnsAsync((Quiz q) => q);
        _repo.Setup(r => r.FindByIdWithQuestionsAsync(It.IsAny<long>())).ReturnsAsync(() => saved);

        var result = await _service.CreateAsync(
            Request(CreateQ(1, 1, "A", " #FF8800 "), CreateQ(2, 1, "A", "#ff8800"), CreateQ(3, 2, "B", null)), creatorId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(["#ff8800", "#ff8800", null], saved!.Preguntas.Select(p => p.FaseColor));
        Assert.Equal("#ff8800", result.Value.Preguntas[0].FaseColor);
    }

    [Theory]
    [InlineData("rojo")]
    [InlineData("#fff")]
    [InlineData("ff0000")]
    [InlineData("#gg0000")]
    public async Task CreateAsync_InvalidPhaseColor_ReturnsValidationError(string color)
    {
        var result = await _service.CreateAsync(Request(CreateQ(1, 1, null, color)), creatorId: 1);

        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_SamePhaseWithDifferentColors_ReturnsValidationError()
    {
        var result = await _service.CreateAsync(
            Request(CreateQ(1, 1, "A", "#ff0000"), CreateQ(2, 1, "A", "#00ff00")), creatorId: 1);

        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_InvalidPhaseColor_IsDroppedInDrafts()
    {
        Quiz? saved = null;
        _repo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).Callback<Quiz>(q => saved = q).ReturnsAsync((Quiz q) => q);
        _repo.Setup(r => r.FindByIdWithQuestionsAsync(It.IsAny<long>())).ReturnsAsync(() => saved);

        var result = await _service.CreateAsync(Request(CreateQ(1, 1, null, "rojo")) with { EsBorrador = true }, creatorId: 1);

        Assert.True(result.IsSuccess);
        Assert.Null(saved!.Preguntas.Single().FaseColor);
    }

    [Fact]
    public void LegacyVersionContentWithoutColor_DeserializesToNull()
    {
        const string legacy = """{"Nombre":"Viejo","Preguntas":[{"NumeroPregunta":1,"Enunciado":"P","FaseNumero":2,"Respuestas":[]}]}""";

        var content = JsonSerializer.Deserialize<UpdateQuizRequest>(legacy, JsonSerializerOptions.Web);

        Assert.Null(content!.Preguntas[0].FaseColor);
    }

    [Fact]
    public async Task CreateAsync_DraftSkipsPhaseValidation()
    {
        Quiz? saved = null;
        _repo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).Callback<Quiz>(q => saved = q).ReturnsAsync((Quiz q) => q);
        _repo.Setup(r => r.FindByIdWithQuestionsAsync(It.IsAny<long>())).ReturnsAsync(() => saved);
        var request = Request(CreateQ(1, 3, null)) with { EsBorrador = true };

        var result = await _service.CreateAsync(request, creatorId: 1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateAsync_DraftOfPublishedQuiz_KeepsPhasesInStoredContent()
    {
        var quiz = new Quiz { Id = 1, Nombre = "Publicado", CreatorId = 1, VersionPublicada = 1, GameCode = "ABC123" };
        _repo.Setup(r => r.FindByIdWithQuestionsAsync(1)).ReturnsAsync(quiz);
        _repo.Setup(r => r.FindDraftAsync(1)).ReturnsAsync((QuizVersion?)null);
        QuizVersion? stored = null;
        _repo.Setup(r => r.SaveDraftAsync(It.IsAny<QuizVersion>()))
            .Callback<QuizVersion>(v => stored = v).ReturnsAsync((QuizVersion v) => v);

        var request = new UpdateQuizRequest
        {
            Nombre = "Editado",
            EsBorrador = true,
            Preguntas =
            [
                new UpdatePreguntaRequest
                {
                    NumeroPregunta = 1, Enunciado = "P1", FaseNumero = 1, FaseNombre = "Ronda 1",
                    Respuestas = [new UpdateRespuestaRequest { Texto = "A", EsCorrecta = true }, new UpdateRespuestaRequest { Texto = "B" }]
                },
                new UpdatePreguntaRequest
                {
                    NumeroPregunta = 2, Enunciado = "P2", FaseNumero = 2, FaseNombre = "Ronda 2",
                    Respuestas = [new UpdateRespuestaRequest { Texto = "A", EsCorrecta = true }, new UpdateRespuestaRequest { Texto = "B" }]
                }
            ]
        };

        var result = await _service.UpdateAsync(1, request, userId: 1);

        Assert.True(result.IsSuccess);

        var content = JsonSerializer.Deserialize<UpdateQuizRequest>(stored!.Contenido, JsonSerializerOptions.Web);
        Assert.Equal([1, 2], content!.Preguntas.Select(p => p.FaseNumero));
        Assert.Equal(["Ronda 1", "Ronda 2"], content.Preguntas.Select(p => p.FaseNombre));
    }

    private static CreatePreguntaRequest WithDificultad(string? dificultad) => CreateQ(1, 1, null) with { Dificultad = dificultad };

    [Theory]
    [InlineData("facil", "facil")]
    [InlineData(" Dificil ", "dificil")]
    [InlineData(null, null)]
    public async Task CreateAsync_Difficulty_IsStoredNormalized(string? enviada, string? esperada)
    {
        Quiz? saved = null;
        _repo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).Callback<Quiz>(q => saved = q).ReturnsAsync((Quiz q) => q);
        _repo.Setup(r => r.FindByIdWithQuestionsAsync(It.IsAny<long>())).ReturnsAsync(() => saved);

        var result = await _service.CreateAsync(Request(WithDificultad(enviada)), creatorId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(esperada, saved!.Preguntas.Single().Dificultad);
        Assert.Equal(esperada, result.Value.Preguntas.Single().Dificultad);
    }

    [Fact]
    public async Task CreateAsync_UnknownDifficulty_ReturnsValidationErrorWhenPublishing()
    {
        var result = await _service.CreateAsync(Request(WithDificultad("imposible")), creatorId: 1);

        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_UnknownDifficulty_IsDroppedInDrafts()
    {
        Quiz? saved = null;
        _repo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).Callback<Quiz>(q => saved = q).ReturnsAsync((Quiz q) => q);
        _repo.Setup(r => r.FindByIdWithQuestionsAsync(It.IsAny<long>())).ReturnsAsync(() => saved);

        var result = await _service.CreateAsync(Request(WithDificultad("imposible")) with { EsBorrador = true }, creatorId: 1);

        Assert.True(result.IsSuccess);
        Assert.Null(saved!.Preguntas.Single().Dificultad);
    }

    [Fact]
    public async Task UpdateAsync_DraftOfPublishedQuiz_KeepsDifficultyInStoredContent()
    {
        var quiz = new Quiz { Id = 1, Nombre = "Publicado", CreatorId = 1, VersionPublicada = 1, GameCode = "ABC123" };
        _repo.Setup(r => r.FindByIdWithQuestionsAsync(1)).ReturnsAsync(quiz);
        _repo.Setup(r => r.FindDraftAsync(1)).ReturnsAsync((QuizVersion?)null);
        QuizVersion? stored = null;
        _repo.Setup(r => r.SaveDraftAsync(It.IsAny<QuizVersion>()))
            .Callback<QuizVersion>(v => stored = v).ReturnsAsync((QuizVersion v) => v);

        var request = new UpdateQuizRequest
        {
            Nombre = "Editado",
            EsBorrador = true,
            Preguntas =
            [
                new UpdatePreguntaRequest
                {
                    NumeroPregunta = 1, Enunciado = "P1", Dificultad = "media",
                    Respuestas = [new UpdateRespuestaRequest { Texto = "A", EsCorrecta = true }, new UpdateRespuestaRequest { Texto = "B" }]
                }
            ]
        };

        Assert.True((await _service.UpdateAsync(1, request, userId: 1)).IsSuccess);

        var content = JsonSerializer.Deserialize<UpdateQuizRequest>(stored!.Contenido, JsonSerializerOptions.Web);
        Assert.Equal("media", content!.Preguntas.Single().Dificultad);
    }

    [Fact]
    public void FromDraft_CarriesDifficultyToTheResponse()
    {
        var quiz = new Quiz { Id = 1, Nombre = "Q", CreatorId = 1, GameCode = "ABC123", VersionPublicada = 1 };
        var contenido = new UpdateQuizRequest
        {
            Nombre = "Q",
            Preguntas =
            [
                new UpdatePreguntaRequest { NumeroPregunta = 1, Enunciado = "P", Dificultad = "Dificil" },
                new UpdatePreguntaRequest { NumeroPregunta = 2, Enunciado = "Q", Dificultad = "rara" }
            ]
        };

        var response = QuizResponse.FromDraft(quiz, contenido, DateTime.UtcNow);

        Assert.Equal(["dificil", null], response.Preguntas.Select(p => p.Dificultad));
    }

    [Fact]
    public void LegacyVersionContentWithoutPhases_DeserializesToPhaseOne()
    {
        const string legacy = """{"Nombre":"Viejo","Preguntas":[{"NumeroPregunta":1,"Enunciado":"P","Respuestas":[]}]}""";

        var content = JsonSerializer.Deserialize<UpdateQuizRequest>(legacy, JsonSerializerOptions.Web);

        Assert.Equal(1, content!.Preguntas[0].FaseNumero);
        Assert.Null(content.Preguntas[0].FaseNombre);
    }
}
