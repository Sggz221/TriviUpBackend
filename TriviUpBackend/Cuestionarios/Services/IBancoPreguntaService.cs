using CSharpFunctionalExtensions;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Cuestionarios.Services;

/// <summary>
/// Servicio del banco personal de preguntas.
/// </summary>
public interface IBancoPreguntaService
{
    Task<Result<BancoPreguntaListResponse, QuizError>> ListAsync(long userId, string? search, string? etiqueta, int page, int pageSize);
    Task<Result<List<EtiquetaCountResponse>, QuizError>> GetEtiquetasAsync(long userId);
    Task<Result<BancoPreguntaResponse, QuizError>> GetByIdAsync(long id, long userId);
    Task<Result<BancoPreguntaResponse, QuizError>> CreateAsync(BancoPreguntaRequest request, long userId);
    Task<Result<BancoPreguntaResponse, QuizError>> UpdateAsync(long id, BancoPreguntaRequest request, long userId);
    Task<UnitResult<QuizError>> DeleteAsync(long id, long userId);
}
