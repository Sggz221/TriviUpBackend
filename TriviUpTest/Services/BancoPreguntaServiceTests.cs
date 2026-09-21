using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Database;
using TriviUpBackend.Errors;

namespace TriviUpTest.Services;

public class BancoPreguntaServiceTests : IDisposable
{
    private readonly Context _context;
    private readonly BancoPreguntaService _service;
    private readonly BancoCategoriaService _categorias;

    public BancoPreguntaServiceTests()
    {
        var options = new DbContextOptionsBuilder<Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new Context(options);
        _categorias = new BancoCategoriaService(new BancoCategoriaRepository(_context), new Mock<ILogger<BancoCategoriaService>>().Object);
        _service = new BancoPreguntaService(
            new BancoPreguntaRepository(_context), _categorias, new Mock<ILogger<BancoPreguntaService>>().Object);
    }

    public void Dispose() => _context.Dispose();

    private static BancoPreguntaRequest Request(
        string enunciado = "¿Capital de Francia?", string? dificultad = null, long? categoriaId = null, string? categoriaNombre = null) => new()
    {
        Enunciado = enunciado,
        Dificultad = dificultad,
        CategoriaId = categoriaId,
        CategoriaNombre = categoriaNombre,
        Respuestas =
        [
            new BancoRespuestaRequest { Texto = "París", EsCorrecta = true },
            new BancoRespuestaRequest { Texto = "Roma" }
        ]
    };

    private async Task<long> NuevaCategoria(string nombre, long userId = 1) =>
        (await _categorias.CreateAsync(new BancoCategoriaRequest { Nombre = nombre }, userId)).Value.Id;

    // ===== Alta y validación =====

    [Fact]
    public async Task CreateAsync_ValidRequest_StoresAnswers()
    {
        var result = await _service.CreateAsync(Request(), userId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(["París", "Roma"], result.Value.Respuestas.Select(r => r.Texto));
        Assert.True(result.Value.Respuestas[0].EsCorrecta);
        Assert.Null(result.Value.CategoriaId);
        Assert.Null(result.Value.Dificultad);
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

    // ===== Dificultad =====

    [Theory]
    [InlineData("facil", "facil")]
    [InlineData(" MEDIA ", "media")]
    [InlineData("Dificil", "dificil")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task CreateAsync_Difficulty_IsNormalized(string? enviada, string? esperada)
    {
        var result = await _service.CreateAsync(Request(dificultad: enviada), userId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(esperada, result.Value.Dificultad);
    }

    [Theory]
    [InlineData("imposible")]
    [InlineData("1")]
    public async Task CreateAsync_UnknownDifficulty_ReturnsValidationError(string dificultad)
    {
        var result = await _service.CreateAsync(Request(dificultad: dificultad), userId: 1);

        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task ListAsync_FiltersByDifficulty()
    {
        await _service.CreateAsync(Request("a", "facil"), userId: 1);
        await _service.CreateAsync(Request("b", "dificil"), userId: 1);
        await _service.CreateAsync(Request("c"), userId: 1);

        var facil = await _service.ListAsync(1, null, null, false, "facil", 1, 20);

        Assert.Equal("a", facil.Value.Preguntas.Single().Enunciado);
        Assert.IsType<QuizValidationError>((await _service.ListAsync(1, null, null, false, "rara", 1, 20)).Error);
    }

    // ===== Categoría en la pregunta =====

    [Fact]
    public async Task CreateAsync_WithCategoryId_AssignsExistingCategory()
    {
        var categoriaId = await NuevaCategoria("Geografía");

        var result = await _service.CreateAsync(Request(categoriaId: categoriaId), userId: 1);

        Assert.Equal(categoriaId, result.Value.CategoriaId);
        Assert.Equal("Geografía", result.Value.CategoriaNombre);
    }

    [Fact]
    public async Task CreateAsync_WithCategoryName_CreatesItOnTheFlyAndReusesItAfterwards()
    {
        var primera = await _service.CreateAsync(Request(categoriaNombre: "  Historia "), userId: 1);
        var segunda = await _service.CreateAsync(Request("otra", categoriaNombre: "historia"), userId: 1);

        Assert.Equal("Historia", primera.Value.CategoriaNombre);
        Assert.Equal(primera.Value.CategoriaId, segunda.Value.CategoriaId);
        Assert.Single((await _categorias.ListAsync(1)).Value.Categorias);
    }

    [Fact]
    public async Task CreateAsync_CategoryOfAnotherUser_ReturnsNotFound()
    {
        var ajena = await NuevaCategoria("Ajena", userId: 2);

        var result = await _service.CreateAsync(Request(categoriaId: ajena), userId: 1);

        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_CategoryIdWinsOverName()
    {
        var categoriaId = await NuevaCategoria("Ciencia");

        var result = await _service.CreateAsync(Request(categoriaId: categoriaId, categoriaNombre: "Otra"), userId: 1);

        Assert.Equal("Ciencia", result.Value.CategoriaNombre);
        Assert.Single((await _categorias.ListAsync(1)).Value.Categorias);
    }

    [Fact]
    public async Task UpdateAsync_CanMoveAndClearCategory()
    {
        var a = await NuevaCategoria("A");
        var creada = (await _service.CreateAsync(Request(categoriaId: a), userId: 1)).Value;

        var sinCategoria = await _service.UpdateAsync(creada.Id, Request(), userId: 1);

        Assert.Null(sinCategoria.Value.CategoriaId);
        Assert.Null(sinCategoria.Value.CategoriaNombre);
    }

    // ===== Listado y filtros =====

    [Fact]
    public async Task ListAsync_OnlyReturnsOwnQuestions()
    {
        await _service.CreateAsync(Request("Mía"), userId: 1);
        await _service.CreateAsync(Request("Ajena"), userId: 2);

        var result = await _service.ListAsync(1, null, null, false, null, 1, 20);

        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal("Mía", result.Value.Preguntas.Single().Enunciado);
    }

    [Fact]
    public async Task ListAsync_FiltersByCategoryAndUncategorized()
    {
        var geo = await NuevaCategoria("Geografía");
        await _service.CreateAsync(Request("Capital de Francia", categoriaId: geo), userId: 1);
        await _service.CreateAsync(Request("Capital de Japón", categoriaId: geo), userId: 1);
        await _service.CreateAsync(Request("Fórmula del agua"), userId: 1);

        Assert.Equal(2, (await _service.ListAsync(1, null, geo, false, null, 1, 20)).Value.TotalCount);
        var sin = await _service.ListAsync(1, null, null, true, null, 1, 20);
        Assert.Equal("Fórmula del agua", sin.Value.Preguntas.Single().Enunciado);
        Assert.Equal(1, (await _service.ListAsync(1, "japón", geo, false, null, 1, 20)).Value.TotalCount);
    }

    [Fact]
    public async Task ListAsync_SearchAlsoMatchesCategoryName()
    {
        var geo = await NuevaCategoria("Geografía");
        await _service.CreateAsync(Request("Capital de Francia", categoriaId: geo), userId: 1);
        await _service.CreateAsync(Request("Fórmula del agua"), userId: 1);

        var result = await _service.ListAsync(1, "geograf", null, false, null, 1, 20);

        Assert.Equal("Capital de Francia", result.Value.Preguntas.Single().Enunciado);
    }

    [Fact]
    public async Task ListAsync_CombinesCategoryAndDifficulty()
    {
        var geo = await NuevaCategoria("Geografía");
        await _service.CreateAsync(Request("fácil geo", "facil", geo), userId: 1);
        await _service.CreateAsync(Request("difícil geo", "dificil", geo), userId: 1);
        await _service.CreateAsync(Request("fácil sin", "facil"), userId: 1);

        var result = await _service.ListAsync(1, null, geo, false, "facil", 1, 20);

        Assert.Equal("fácil geo", result.Value.Preguntas.Single().Enunciado);
    }

    [Fact]
    public async Task ListAsync_Paginates()
    {
        for (var i = 0; i < 5; i++) await _service.CreateAsync(Request($"P{i}"), userId: 1);

        var page2 = await _service.ListAsync(1, null, null, false, null, page: 2, pageSize: 2);

        Assert.Equal(5, page2.Value.TotalCount);
        Assert.Equal(2, page2.Value.Preguntas.Count);
    }

    // ===== Asignar categoría en bloque =====

    [Fact]
    public async Task AsignarCategoriaAsync_MovesOnlyOwnQuestions()
    {
        var mia = await NuevaCategoria("Mía", userId: 1);
        var p1 = (await _service.CreateAsync(Request("p1"), userId: 1)).Value.Id;
        var p2 = (await _service.CreateAsync(Request("p2"), userId: 1)).Value.Id;
        var ajena = (await _service.CreateAsync(Request("ajena"), userId: 2)).Value.Id;

        var result = await _service.AsignarCategoriaAsync(
            new AsignarCategoriaRequest { PreguntaIds = [p1, p2, ajena, 999], CategoriaId = mia }, userId: 1);

        Assert.Equal(2, result.Value.Actualizadas);
        Assert.Equal(mia, (await _service.GetByIdAsync(p1, 1)).Value.CategoriaId);
        Assert.Equal(mia, (await _service.GetByIdAsync(p2, 1)).Value.CategoriaId);
        Assert.Null((await _service.GetByIdAsync(ajena, 2)).Value.CategoriaId);
    }

    [Fact]
    public async Task AsignarCategoriaAsync_NullCategory_ClearsIt()
    {
        var cat = await NuevaCategoria("X");
        var p = (await _service.CreateAsync(Request(categoriaId: cat), userId: 1)).Value.Id;

        await _service.AsignarCategoriaAsync(new AsignarCategoriaRequest { PreguntaIds = [p], CategoriaId = null }, userId: 1);

        Assert.Null((await _service.GetByIdAsync(p, 1)).Value.CategoriaId);
    }

    [Fact]
    public async Task AsignarCategoriaAsync_CategoryOfAnotherUser_ReturnsNotFound()
    {
        var ajena = await NuevaCategoria("Ajena", userId: 2);
        var p = (await _service.CreateAsync(Request(), userId: 1)).Value.Id;

        var result = await _service.AsignarCategoriaAsync(new AsignarCategoriaRequest { PreguntaIds = [p], CategoriaId = ajena }, userId: 1);

        Assert.IsType<QuizNotFoundError>(result.Error);
        Assert.Null((await _service.GetByIdAsync(p, 1)).Value.CategoriaId);
    }

    [Fact]
    public async Task AsignarCategoriaAsync_WithoutQuestions_ReturnsValidationError()
    {
        var result = await _service.AsignarCategoriaAsync(new AsignarCategoriaRequest { PreguntaIds = [] }, userId: 1);

        Assert.IsType<QuizValidationError>(result.Error);
    }

    // ===== Edición y borrado =====

    [Fact]
    public async Task UpdateAsync_Owner_ReplacesContent()
    {
        var created = (await _service.CreateAsync(Request("Vieja", "facil"), userId: 1)).Value;

        var result = await _service.UpdateAsync(created.Id, Request("Nueva", "media"), userId: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal("Nueva", result.Value.Enunciado);
        Assert.Equal("media", result.Value.Dificultad);
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
