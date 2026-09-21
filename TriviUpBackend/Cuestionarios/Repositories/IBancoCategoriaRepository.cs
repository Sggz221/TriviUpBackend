using TriviUpBackend.Cuestionarios.Entities;

namespace TriviUpBackend.Cuestionarios.Repositories;

/// <summary>
/// Acceso a datos de las categorías del banco de preguntas.
/// </summary>
public interface IBancoCategoriaRepository
{
    Task<BancoCategoria?> FindByIdAsync(long id);

    /// <summary>Categoría del usuario con ese nombre, sin distinguir mayúsculas.</summary>
    Task<BancoCategoria?> FindByNombreAsync(long creatorId, string nombre);

    /// <summary>Categorías del usuario con su número de preguntas, ordenadas por nombre.</summary>
    Task<List<(BancoCategoria Categoria, int Total)>> FindByCreatorWithCountsAsync(long creatorId);

    /// <summary>Preguntas del usuario que no tienen categoría.</summary>
    Task<int> CountSinCategoriaAsync(long creatorId);

    Task<BancoCategoria> AddAsync(BancoCategoria categoria);
    Task<BancoCategoria> UpdateAsync(BancoCategoria categoria);

    /// <summary>Borra la categoría; sus preguntas se conservan y quedan sin categoría.</summary>
    Task DeleteAsync(BancoCategoria categoria);
}
