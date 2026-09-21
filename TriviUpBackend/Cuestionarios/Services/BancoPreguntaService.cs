using CSharpFunctionalExtensions;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Cuestionarios.Services;

/// <inheritdoc />
public class BancoPreguntaService(
    IBancoPreguntaRepository repository,
    IBancoCategoriaService categoriaService,
    ILogger<BancoPreguntaService> logger
) : IBancoPreguntaService
{
    public async Task<Result<BancoPreguntaListResponse, QuizError>> ListAsync(
        long userId, string? search, long? categoriaId, bool sinCategoria, string? dificultad, int page, int pageSize)
    {
        if (!Dificultades.EsValida(dificultad))
        {
            return Result.Failure<BancoPreguntaListResponse, QuizError>(new QuizValidationError("Dificultad no válida"));
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await repository.FindByCreatorAsync(userId, search, categoriaId, sinCategoria, dificultad, page, pageSize);
        return Result.Success<BancoPreguntaListResponse, QuizError>(
            new BancoPreguntaListResponse(items.Select(BancoPreguntaResponse.FromEntity).ToList(), total));
    }

    public async Task<Result<BancoPreguntaResponse, QuizError>> GetByIdAsync(long id, long userId)
    {
        var owned = await FindOwnedAsync(id, userId);
        return owned.IsFailure
            ? Result.Failure<BancoPreguntaResponse, QuizError>(owned.Error)
            : Result.Success<BancoPreguntaResponse, QuizError>(BancoPreguntaResponse.FromEntity(owned.Value));
    }

    public async Task<Result<BancoPreguntaResponse, QuizError>> CreateAsync(BancoPreguntaRequest request, long userId)
    {
        var validation = Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<BancoPreguntaResponse, QuizError>(validation.Error);
        }

        var categoria = await ResolverCategoriaAsync(request, userId);
        if (categoria.IsFailure)
        {
            return Result.Failure<BancoPreguntaResponse, QuizError>(categoria.Error);
        }

        var pregunta = new BancoPregunta { CreatorId = userId };
        Apply(pregunta, request, categoria.Value);

        await repository.AddAsync(pregunta);
        logger.LogInformation("Pregunta {Id} guardada en el banco del usuario {UserId}", pregunta.Id, userId);

        return Result.Success<BancoPreguntaResponse, QuizError>(BancoPreguntaResponse.FromEntity(pregunta));
    }

    public async Task<Result<BancoPreguntaResponse, QuizError>> UpdateAsync(long id, BancoPreguntaRequest request, long userId)
    {
        var owned = await FindOwnedAsync(id, userId);
        if (owned.IsFailure)
        {
            return Result.Failure<BancoPreguntaResponse, QuizError>(owned.Error);
        }

        var validation = Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<BancoPreguntaResponse, QuizError>(validation.Error);
        }

        var categoria = await ResolverCategoriaAsync(request, userId);
        if (categoria.IsFailure)
        {
            return Result.Failure<BancoPreguntaResponse, QuizError>(categoria.Error);
        }

        Apply(owned.Value, request, categoria.Value);
        await repository.UpdateAsync(owned.Value);

        return Result.Success<BancoPreguntaResponse, QuizError>(BancoPreguntaResponse.FromEntity(owned.Value));
    }

    public async Task<UnitResult<QuizError>> DeleteAsync(long id, long userId)
    {
        var owned = await FindOwnedAsync(id, userId);
        if (owned.IsFailure)
        {
            return UnitResult.Failure(owned.Error);
        }

        await repository.DeleteAsync(owned.Value);
        return UnitResult.Success<QuizError>();
    }

    public async Task<Result<AsignarCategoriaResponse, QuizError>> AsignarCategoriaAsync(AsignarCategoriaRequest request, long userId)
    {
        var ids = request.PreguntaIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Failure<AsignarCategoriaResponse, QuizError>(new QuizValidationError("Indica al menos una pregunta"));
        }

        BancoCategoria? categoria = null;
        if (request.CategoriaId.HasValue)
        {
            var owned = await categoriaService.GetOwnedAsync(request.CategoriaId.Value, userId);
            if (owned.IsFailure)
            {
                return Result.Failure<AsignarCategoriaResponse, QuizError>(owned.Error);
            }
            categoria = owned.Value;
        }

        // Solo se tocan las preguntas del usuario; los ids ajenos o inexistentes se ignoran
        var preguntas = await repository.FindByIdsAsync(userId, ids);
        foreach (var pregunta in preguntas)
        {
            pregunta.CategoriaId = categoria?.Id;
            pregunta.Categoria = categoria;
        }

        await repository.UpdateRangeAsync(preguntas);
        return Result.Success<AsignarCategoriaResponse, QuizError>(new AsignarCategoriaResponse(preguntas.Count));
    }

    /// <summary>Busca la pregunta y comprueba que sea del usuario (404 si no existe o es de otro, para no revelar ids ajenos).</summary>
    private async Task<Result<BancoPregunta, QuizError>> FindOwnedAsync(long id, long userId)
    {
        var pregunta = await repository.FindByIdAsync(id);
        if (pregunta is null || pregunta.CreatorId != userId)
        {
            return Result.Failure<BancoPregunta, QuizError>(new QuizNotFoundError("Pregunta no encontrada"));
        }

        return Result.Success<BancoPregunta, QuizError>(pregunta);
    }

    /// <summary>Categoría por id (debe ser del usuario) o por nombre (existente o nueva); null si no se indica ninguna.</summary>
    private async Task<Result<BancoCategoria?, QuizError>> ResolverCategoriaAsync(BancoPreguntaRequest request, long userId)
    {
        if (request.CategoriaId.HasValue)
        {
            var owned = await categoriaService.GetOwnedAsync(request.CategoriaId.Value, userId);
            return owned.IsFailure
                ? Result.Failure<BancoCategoria?, QuizError>(owned.Error)
                : Result.Success<BancoCategoria?, QuizError>(owned.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.CategoriaNombre))
        {
            var porNombre = await categoriaService.FindOrCreateAsync(request.CategoriaNombre, userId);
            return porNombre.IsFailure
                ? Result.Failure<BancoCategoria?, QuizError>(porNombre.Error)
                : Result.Success<BancoCategoria?, QuizError>(porNombre.Value);
        }

        return Result.Success<BancoCategoria?, QuizError>(null);
    }

    private static void Apply(BancoPregunta pregunta, BancoPreguntaRequest request, BancoCategoria? categoria)
    {
        pregunta.Enunciado = request.Enunciado.Trim();
        pregunta.ImagenUrl = string.IsNullOrWhiteSpace(request.ImagenUrl) ? null : request.ImagenUrl;
        pregunta.Respuestas = request.Respuestas
            .Select(r => new BancoRespuesta { Texto = r.Texto.Trim(), EsCorrecta = r.EsCorrecta })
            .ToList();
        pregunta.Dificultad = Dificultades.Normalizar(request.Dificultad);
        pregunta.CategoriaId = categoria?.Id;
        pregunta.Categoria = categoria;
    }

    private static UnitResult<QuizError> Validate(BancoPreguntaRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Enunciado))
        {
            return UnitResult.Failure<QuizError>(new QuizValidationError("La pregunta no puede estar vacía"));
        }

        if (request.Respuestas == null || request.Respuestas.Count < 2 ||
            request.Respuestas.Any(r => string.IsNullOrWhiteSpace(r.Texto)))
        {
            return UnitResult.Failure<QuizError>(new QuizValidationError("La pregunta debe tener al menos 2 respuestas con texto"));
        }

        if (request.Respuestas.Count(r => r.EsCorrecta) != 1)
        {
            return UnitResult.Failure<QuizError>(new QuizValidationError("La pregunta debe tener exactamente una respuesta correcta"));
        }

        if (!Dificultades.EsValida(request.Dificultad))
        {
            return UnitResult.Failure<QuizError>(new QuizValidationError("Dificultad no válida. Usa facil, media o dificil."));
        }

        return UnitResult.Success<QuizError>();
    }
}
