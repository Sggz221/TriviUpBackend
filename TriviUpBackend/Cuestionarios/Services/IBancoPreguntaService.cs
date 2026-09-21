using CSharpFunctionalExtensions;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Cuestionarios.Services;

/// <summary>
/// Servicio del banco personal de preguntas.
/// </summary>
public interface IBancoPreguntaService
{
    Task<Result<BancoPreguntaListResponse, QuizError>> ListAsync(
        long userId, string? search, long? categoriaId, bool sinCategoria, string? dificultad, int page, int pageSize);
    Task<Result<BancoPreguntaResponse, QuizError>> GetByIdAsync(long id, long userId);
    Task<Result<BancoPreguntaResponse, QuizError>> CreateAsync(BancoPreguntaRequest request, long userId);
    Task<Result<BancoPreguntaResponse, QuizError>> UpdateAsync(long id, BancoPreguntaRequest request, long userId);
    Task<UnitResult<QuizError>> DeleteAsync(long id, long userId);

    /// <summary>Mueve varias preguntas propias a una categoría (o las deja sin categoría si es null).</summary>
    Task<Result<AsignarCategoriaResponse, QuizError>> AsignarCategoriaAsync(AsignarCategoriaRequest request, long userId);
}
