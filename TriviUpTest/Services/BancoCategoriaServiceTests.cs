using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Database;
using TriviUpBackend.Errors;

namespace TriviUpTest.Services;

public class BancoCategoriaServiceTests : IDisposable
{
    private readonly Context _context;
    private readonly BancoCategoriaService _service;
    private readonly BancoPreguntaService _preguntas;

    public BancoCategoriaServiceTests()
    {
        var options = new DbContextOptionsBuilder<Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new Context(options);
        _service = new BancoCategoriaService(new BancoCategoriaRepository(_context), new Mock<ILogger<BancoCategoriaService>>().Object);
        _preguntas = new BancoPreguntaService(
            new BancoPreguntaRepository(_context), _service, new Mock<ILogger<BancoPreguntaService>>().Object);
    }

    public void Dispose() => _context.Dispose();

    private static BancoCategoriaRequest Req(string nombre) => new() { Nombre = nombre };

    private Task<TriviUpBackend.Cuestionarios.DTOs.BancoPreguntaResponse> NuevaPregunta(string enunciado, long? categoriaId, long userId = 1) =>
        _preguntas.CreateAsync(new BancoPreguntaRequest
        {
            Enunciado = enunciado,
            CategoriaId = categoriaId,
            Respuestas = [new BancoRespuestaRequest { Texto = "a", EsCorrecta = true }, new BancoRespuestaRequest { Texto = "b" }]
        }, userId).ContinueWith(t => t.Result.Value);

    [Fact]
    public async Task CreateAsync_TrimsAndCollapsesWhitespace()
    {
        var result = await _service.CreateAsync(Req("  Historia   antigua "), userId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("Historia antigua", result.Value.Nombre);
        Assert.Equal(0, result.Value.Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task CreateAsync_EmptyName_ReturnsValidationError(string nombre)
    {
        Assert.IsType<QuizValidationError>((await _service.CreateAsync(Req(nombre), 1)).Error);
    }

    [Fact]
    public async Task CreateAsync_NameTooLong_ReturnsValidationError()
    {
        var result = await _service.CreateAsync(Req(new string('x', 51)), 1);

        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameIgnoringCase_ReturnsValidationError()
    {
        await _service.CreateAsync(Req("Historia"), 1);

        var result = await _service.CreateAsync(Req("  HISTORIA "), 1);

        Assert.IsType<QuizValidationError>(result.Error);
        Assert.Single((await _service.ListAsync(1)).Value.Categorias);
    }

    [Fact]
    public async Task CreateAsync_SameNameForDifferentUsers_IsAllowed()
    {
        Assert.True((await _service.CreateAsync(Req("Historia"), 1)).IsSuccess);
        Assert.True((await _service.CreateAsync(Req("Historia"), 2)).IsSuccess);
    }

    [Fact]
    public async Task ListAsync_ReturnsCountsSortedByNameAndUncategorizedTotal()
    {
        var b = (await _service.CreateAsync(Req("B"), 1)).Value.Id;
        var a = (await _service.CreateAsync(Req("A"), 1)).Value.Id;
        await _service.CreateAsync(Req("Vacía"), 1);
        await NuevaPregunta("p1", a);
        await NuevaPregunta("p2", a);
        await NuevaPregunta("p3", b);
        await NuevaPregunta("suelta", null);
        await NuevaPregunta("de otro", null, userId: 2);

        var result = (await _service.ListAsync(1)).Value;

        Assert.Equal(["A", "B", "Vacía"], result.Categorias.Select(c => c.Nombre));
        Assert.Equal([2, 1, 0], result.Categorias.Select(c => c.Total));
        Assert.Equal(1, result.SinCategoria);
    }

    [Fact]
    public async Task ListAsync_DoesNotIncludeOtherUsersCategories()
    {
        await _service.CreateAsync(Req("Mía"), 1);
        await _service.CreateAsync(Req("Ajena"), 2);

        Assert.Equal(["Mía"], (await _service.ListAsync(1)).Value.Categorias.Select(c => c.Nombre));
    }

    [Fact]
    public async Task RenameAsync_Owner_ChangesNameAndKeepsCount()
    {
        var id = (await _service.CreateAsync(Req("Vieja"), 1)).Value.Id;
        await NuevaPregunta("p", id);

        var result = await _service.RenameAsync(id, Req("Nueva"), 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("Nueva", result.Value.Nombre);
        Assert.Equal(1, result.Value.Total);
    }

    [Fact]
    public async Task RenameAsync_CanChangeOnlyTheCase()
    {
        var id = (await _service.CreateAsync(Req("historia"), 1)).Value.Id;

        var result = await _service.RenameAsync(id, Req("Historia"), 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("Historia", result.Value.Nombre);
    }

    [Fact]
    public async Task RenameAsync_ToExistingName_ReturnsValidationError()
    {
        await _service.CreateAsync(Req("Uno"), 1);
        var dos = (await _service.CreateAsync(Req("Dos"), 1)).Value.Id;

        Assert.IsType<QuizValidationError>((await _service.RenameAsync(dos, Req("uno"), 1)).Error);
    }

    [Fact]
    public async Task RenameAsync_OtherUsersCategory_ReturnsNotFound()
    {
        var id = (await _service.CreateAsync(Req("Ajena"), 2)).Value.Id;

        var result = await _service.RenameAsync(id, Req("Hack"), 1);

        Assert.IsType<QuizNotFoundError>(result.Error);
        Assert.Equal("Ajena", (await _service.ListAsync(2)).Value.Categorias.Single().Nombre);
    }

    [Fact]
    public async Task DeleteAsync_KeepsQuestionsWithoutCategory()
    {
        var id = (await _service.CreateAsync(Req("Borrar"), 1)).Value.Id;
        var pregunta = await NuevaPregunta("sigue viva", id);

        var result = await _service.DeleteAsync(id, 1);

        Assert.True(result.IsSuccess);
        var viva = (await _preguntas.GetByIdAsync(pregunta.Id, 1)).Value;
        Assert.Null(viva.CategoriaId);
        Assert.Null(viva.CategoriaNombre);
        Assert.Empty((await _service.ListAsync(1)).Value.Categorias);
        Assert.Equal(1, (await _service.ListAsync(1)).Value.SinCategoria);
    }

    [Fact]
    public async Task DeleteAsync_OtherUsersCategory_ReturnsNotFoundAndKeepsIt()
    {
        var id = (await _service.CreateAsync(Req("Ajena"), 2)).Value.Id;

        Assert.True((await _service.DeleteAsync(id, 1)).IsFailure);
        Assert.Single((await _service.ListAsync(2)).Value.Categorias);
    }

    [Fact]
    public async Task FindOrCreateAsync_ReusesExistingIgnoringCase()
    {
        var existente = (await _service.CreateAsync(Req("Ciencia"), 1)).Value.Id;

        var result = await _service.FindOrCreateAsync(" ciencia ", 1);

        Assert.Equal(existente, result.Value.Id);
    }

    [Fact]
    public async Task FindOrCreateAsync_CreatesWhenMissing()
    {
        var result = await _service.FindOrCreateAsync("Nueva", 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("Nueva", (await _service.ListAsync(1)).Value.Categorias.Single().Nombre);
    }
}
