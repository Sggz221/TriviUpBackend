using TriviUpBackend.Cuestionarios.Entities;

namespace TriviUpBackend.Cuestionarios.Repositories;

/// <summary>
/// Acceso a datos del banco de preguntas. Las consultas de lista están acotadas al usuario propietario.
/// </summary>
public interface IBancoPreguntaRepository
{
    Task<BancoPregunta?> FindByIdAsync(long id);

    /// <summary>Página de preguntas del usuario, filtrable por texto y etiqueta.</summary>
    Task<(List<BancoPregunta> Items, int Total)> FindByCreatorAsync(long creatorId, string? search, string? etiqueta, int page, int pageSize);

    /// <summary>Etiquetas (EtiquetasTexto) de todas las preguntas del usuario.</summary>
    Task<List<string>> FindEtiquetasTextosAsync(long creatorId);

    Task<BancoPregunta> AddAsync(BancoPregunta pregunta);
    Task<BancoPregunta> UpdateAsync(BancoPregunta pregunta);
    Task DeleteAsync(BancoPregunta pregunta);

}
