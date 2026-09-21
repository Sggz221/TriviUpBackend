using CSharpFunctionalExtensions;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Cuestionarios.Services;

/// <summary>
/// Servicio de las categorías del banco personal de preguntas.
/// </summary>
public interface IBancoCategoriaService
{
    Task<Result<BancoCategoriasResponse, QuizError>> ListAsync(long userId);
    Task<Result<BancoCategoriaResponse, QuizError>> CreateAsync(BancoCategoriaRequest request, long userId);
    Task<Result<BancoCategoriaResponse, QuizError>> RenameAsync(long id, BancoCategoriaRequest request, long userId);

    /// <summary>Borra la categoría; sus preguntas se conservan sin categoría.</summary>
    Task<UnitResult<QuizError>> DeleteAsync(long id, long userId);

    /// <summary>Categoría del usuario (404 si no existe o es de otro).</summary>
    Task<Result<BancoCategoria, QuizError>> GetOwnedAsync(long id, long userId);

    /// <summary>La categoría del usuario con ese nombre (sin distinguir mayúsculas) o una nueva.</summary>
    Task<Result<BancoCategoria, QuizError>> FindOrCreateAsync(string nombre, long userId);
}
