using CSharpFunctionalExtensions;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Cuestionarios.Services;

/// <inheritdoc />
public class BancoPreguntaService(
    IBancoPreguntaRepository repository,
    ILogger<BancoPreguntaService> logger
) : IBancoPreguntaService
{
    public const int MaxEtiquetas = 10;
    public const int MaxEtiquetaLength = 30;

    public async Task<Result<BancoPreguntaListResponse, QuizError>> ListAsync(
        long userId, string? search, string? etiqueta, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await repository.FindByCreatorAsync(userId, search, etiqueta, page, pageSize);
        return Result.Success<BancoPreguntaListResponse, QuizError>(
            new BancoPreguntaListResponse(items.Select(BancoPreguntaResponse.FromEntity).ToList(), total));
    }

    public async Task<Result<List<EtiquetaCountResponse>, QuizError>> GetEtiquetasAsync(long userId)
    {
        var textos = await repository.FindEtiquetasTextosAsync(userId);
        var counts = textos
            .SelectMany(t => t.Split('|', StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(t => t)
            .Select(g => new EtiquetaCountResponse(g.Key, g.Count()))
            .OrderByDescending(e => e.Total)
            .ThenBy(e => e.Etiqueta, StringComparer.Ordinal)
            .ToList();

        return Result.Success<List<EtiquetaCountResponse>, QuizError>(counts);
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

        var pregunta = new BancoPregunta { CreatorId = userId };
        Apply(pregunta, request);

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

        Apply(owned.Value, request);
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

    private static void Apply(BancoPregunta pregunta, BancoPreguntaRequest request)
    {
        pregunta.Enunciado = request.Enunciado.Trim();
        pregunta.ImagenUrl = string.IsNullOrWhiteSpace(request.ImagenUrl) ? null : request.ImagenUrl;
        pregunta.Respuestas = request.Respuestas
            .Select(r => new BancoRespuesta { Texto = r.Texto.Trim(), EsCorrecta = r.EsCorrecta })
            .ToList();
        pregunta.Etiquetas = NormalizeEtiquetas(request.Etiquetas);
    }

    /// <summary>Minúsculas, sin espacios sobrantes, sin '|' ni duplicados, con límites de número y longitud.</summary>
    public static List<string> NormalizeEtiquetas(IEnumerable<string>? etiquetas) =>
        (etiquetas ?? [])
            .Select(e => e.Replace("|", " ").Trim().ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Select(e => e.Length > MaxEtiquetaLength ? e[..MaxEtiquetaLength] : e)
            .Distinct()
            .Take(MaxEtiquetas)
            .ToList();

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

        return UnitResult.Success<QuizError>();
    }
}
