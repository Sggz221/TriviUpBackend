using TriviUpBackend.Cuestionarios.Entities;

namespace TriviUpBackend.Cuestionarios.Repositories;

/// <summary>
/// Acceso a datos del banco de preguntas. Las consultas de lista están acotadas al usuario propietario.
/// </summary>
public interface IBancoPreguntaRepository
{
    /// <summary>Busca una pregunta por id (con su categoría).</summary>
    Task<BancoPregunta?> FindByIdAsync(long id);

    /// <summary>Preguntas del usuario con los ids indicados (con su categoría).</summary>
    Task<List<BancoPregunta>> FindByIdsAsync(long creatorId, IReadOnlyCollection<long> ids);

    /// <summary>Página de preguntas del usuario, filtrable por texto, categoría (o ninguna) y dificultad.</summary>
    Task<(List<BancoPregunta> Items, int Total)> FindByCreatorAsync(
        long creatorId, string? search, long? categoriaId, bool sinCategoria, string? dificultad, int page, int pageSize);

    Task<BancoPregunta> AddAsync(BancoPregunta pregunta);
    Task<BancoPregunta> UpdateAsync(BancoPregunta pregunta);
    Task UpdateRangeAsync(IEnumerable<BancoPregunta> preguntas);
    Task DeleteAsync(BancoPregunta pregunta);
}
