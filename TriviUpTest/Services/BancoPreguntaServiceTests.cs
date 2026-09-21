using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Database;
using TriviUpBackend.Errors;
using TriviUpBackend.Models.Auth;

namespace TriviUpTest.Services;

public class BancoPreguntaServiceTests : IDisposable
{
    private readonly Context _context;
    private readonly BancoPreguntaService _service;

    public BancoPreguntaServiceTests()
    {
        var options = new DbContextOptionsBuilder<Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new Context(options);
        _service = new BancoPreguntaService(new BancoPreguntaRepository(_context), new Mock<ILogger<BancoPreguntaService>>().Object);
    }

    public void Dispose() => _context.Dispose();

    private static BancoPreguntaRequest Request(string enunciado = "¿Capital de Francia?", params string[] etiquetas) => new()
    {
        Enunciado = enunciado,
        Etiquetas = etiquetas.ToList(),
        Respuestas =
        [
            new BancoRespuestaRequest { Texto = "París", EsCorrecta = true },
            new BancoRespuestaRequest { Texto = "Roma" }
        ]
    };

    [Fact]
    public async Task CreateAsync_ValidRequest_StoresAnswersAndNormalizedTags()
    {
        var result = await _service.CreateAsync(Request(etiquetas: [" Geografía ", "EUROPA", "europa", "", "a|b"]), userId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(["geografía", "europa", "a b"], result.Value.Etiquetas);
        Assert.Equal(["París", "Roma"], result.Value.Respuestas.Select(r => r.Texto));
        Assert.True(result.Value.Respuestas[0].EsCorrecta);
    }

    [Fact]
    public async Task CreateAsync_LimitsTagCountAndLength()
    {
        var tags = Enumerable.Range(1, 15).Select(i => new string('x', 40) + i).ToArray();

        var result = await _service.CreateAsync(Request(etiquetas: tags), userId: 1);

        Assert.Single(result.Value.Etiquetas); // los 15 se recortan a 30 caracteres y colapsan en uno
        Assert.All(result.Value.Etiquetas, t => Assert.True(t.Length <= BancoPreguntaService.MaxEtiquetaLength));
        Assert.True(BancoPreguntaService.NormalizeEtiquetas(Enumerable.Range(1, 15).Select(i => $"t{i}")).Count <= BancoPreguntaService.MaxEtiquetas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptyStatement_ReturnsValidationError(string enunciado)
    {
        var result = await _service.CreateAsync(Request(enunciado), userId: 1);

        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_WithoutExactlyOneCorrectAnswer_ReturnsValidationError()
    {
        var request = Request() with
        {
            Respuestas =
            [
                new BancoRespuestaRequest { Texto = "A", EsCorrecta = true },
                new BancoRespuestaRequest { Texto = "B", EsCorrecta = true }
            ]
        };

        var result = await _service.CreateAsync(request, userId: 1);

        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task ListAsync_OnlyReturnsOwnQuestions()
    {
        await _service.CreateAsync(Request("Mía"), userId: 1);
        await _service.CreateAsync(Request("Ajena"), userId: 2);

        var result = await _service.ListAsync(1, null, null, 1, 20);

        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal("Mía", result.Value.Preguntas.Single().Enunciado);
    }

    [Fact]
    public async Task ListAsync_FiltersBySearchAndExactTag()
    {
        await _service.CreateAsync(Request("Capital de Francia", "geografia", "europa"), userId: 1);
        await _service.CreateAsync(Request("Capital de Japón", "geografia", "asia"), userId: 1);
        await _service.CreateAsync(Request("Fórmula del agua", "quimica"), userId: 1);

        Assert.Equal(2, (await _service.ListAsync(1, null, "geografia", 1, 20)).Value.TotalCount);
        Assert.Equal(1, (await _service.ListAsync(1, null, "asia", 1, 20)).Value.TotalCount);
        Assert.Equal(0, (await _service.ListAsync(1, null, "geo", 1, 20)).Value.TotalCount); // la etiqueta es exacta
        Assert.Equal(2, (await _service.ListAsync(1, "capital", null, 1, 20)).Value.TotalCount);
        Assert.Equal(1, (await _service.ListAsync(1, "japón", "geografia", 1, 20)).Value.TotalCount);
    }

    [Fact]
    public async Task ListAsync_Paginates()
    {
        for (var i = 0; i < 5; i++) await _service.CreateAsync(Request($"P{i}"), userId: 1);

        var page2 = await _service.ListAsync(1, null, null, page: 2, pageSize: 2);

        Assert.Equal(5, page2.Value.TotalCount);
        Assert.Equal(2, page2.Value.Preguntas.Count);
    }

    [Fact]
    public async Task GetEtiquetasAsync_CountsTagsOfCurrentUserOnly()
    {
        await _service.CreateAsync(Request("a", "geografia", "europa"), userId: 1);
        await _service.CreateAsync(Request("b", "geografia"), userId: 1);
        await _service.CreateAsync(Request("c", "secreta"), userId: 2);

        var result = await _service.GetEtiquetasAsync(1);

        Assert.Equal([new EtiquetaCountResponse("geografia", 2), new EtiquetaCountResponse("europa", 1)], result.Value);
    }

    [Fact]
    public async Task UpdateAsync_Owner_ReplacesContent()
    {
        var created = (await _service.CreateAsync(Request("Vieja", "a"), userId: 1)).Value;

        var result = await _service.UpdateAsync(created.Id, Request("Nueva", "b"), userId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("Nueva", result.Value.Enunciado);
        Assert.Equal(["b"], result.Value.Etiquetas);
    }

    [Fact]
    public async Task UpdateAsync_OtherUsersQuestion_ReturnsNotFound()
    {
        var created = (await _service.CreateAsync(Request(), userId: 1)).Value;

        var result = await _service.UpdateAsync(created.Id, Request("Hack"), userId: 2);

        Assert.IsType<QuizNotFoundError>(result.Error);
        Assert.Equal("¿Capital de Francia?", (await _service.GetByIdAsync(created.Id, 1)).Value.Enunciado);
    }

    [Fact]
    public async Task DeleteAsync_OtherUsersQuestion_ReturnsNotFoundAndKeepsIt()
    {
        var created = (await _service.CreateAsync(Request(), userId: 1)).Value;

        var result = await _service.DeleteAsync(created.Id, userId: 2);

        Assert.True(result.IsFailure);
        Assert.True((await _service.GetByIdAsync(created.Id, 1)).IsSuccess);
    }

    [Fact]
    public async Task DeleteAsync_Owner_RemovesQuestion()
    {
        var created = (await _service.CreateAsync(Request(), userId: 1)).Value;

        Assert.True((await _service.DeleteAsync(created.Id, userId: 1)).IsSuccess);
        Assert.True((await _service.GetByIdAsync(created.Id, 1)).IsFailure);
    }
}
