using CSharpFunctionalExtensions;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Cuestionarios.Services;

/// <summary>
/// Interfaz del servicio de quizzes.
/// Proporciona métodos para crear, consultar, actualizar y eliminar quizzes.
/// </summary>
public interface IQuizService
{
    /// <summary>
    /// Crea un nuevo quiz.
    /// </summary>
    /// <param name="request">Datos del quiz a crear.</param>
    /// <param name="creatorId">ID del usuario creador.</param>
    /// <returns>Resultado con el quiz creado o error.</returns>
    Task<Result<QuizResponse, QuizError>> CreateAsync(CreateQuizRequest request, long creatorId);

    /// <summary>
    /// Obtiene un quiz por su ID.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Resultado con el quiz o error si no existe.</returns>
    Task<Result<QuizResponse, QuizError>> GetByIdAsync(long id);

    /// <summary>
    /// Obtiene un quiz por su código de juego.
    /// </summary>
    /// <param name="gameCode">Código único del quiz.</param>
    /// <returns>Resultado con el quiz o error si no existe.</returns>
    Task<Result<QuizResponse, QuizError>> GetByGameCodeAsync(string gameCode);

    /// <summary>
    /// Obtiene todos los quizzes de forma paginada.
    /// </summary>
    /// <param name="page">Número de página.</param>
    /// <param name="pageSize">Tamaño de página.</param>
    /// <returns>Tupla con lista de quizzes y total de elementos.</returns>
    Task<Result<(List<QuizResponse> Quizzes, int TotalCount), QuizError>> GetAllAsync(int page = 1, int pageSize = 10);

    /// <summary>
    /// Obtiene todos los quizzes creados por un usuario.
    /// </summary>
    /// <param name="creatorId">ID del creador.</param>
    /// <returns>Lista de quizzes del creador.</returns>
    Task<Result<List<QuizResponse>, QuizError>> GetByCreatorIdAsync(long creatorId);

    /// <summary>
    /// Actualiza un quiz existente.
    /// </summary>
    /// <param name="id">ID del quiz a actualizar.</param>
    /// <param name="request">Nuevos datos del quiz.</param>
    /// <param name="userId">ID del usuario que actualiza.</param>
    /// <returns>Resultado con el quiz actualizado o error.</returns>
    Task<Result<QuizResponse, QuizError>> UpdateAsync(long id, UpdateQuizRequest request, long userId);

    /// <summary>
    /// Elimina un quiz.
    /// </summary>
    /// <param name="id">ID del quiz a eliminar.</param>
    /// <param name="userId">ID del usuario que elimina.</param>
    /// <returns>Resultado con éxito o error.</returns>
    Task<UnitResult<QuizError>> DeleteAsync(long id, long userId);

    /// <summary>
    /// Obtiene quizzes públicos con filtros y paginación.
    /// </summary>
    /// <param name="search">Texto de búsqueda (opcional).</param>
    /// <param name="page">Número de página.</param>
    /// <param name="pageSize">Tamaño de página.</param>
    /// <returns>Tupla con lista de quizzes públicos y total.</returns>
    Task<Result<(List<PublicQuizResponse> Quizzes, int TotalCount), QuizError>> GetPublicQuizzesAsync(string? search, int page, int pageSize);

    /// <summary>
    /// Incrementa el contador de likes de un quiz.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Nuevo conteo de likes o error.</returns>
    Task<Result<int, QuizError>> IncrementLikesAsync(long id);

    /// <summary>
    /// Decrementa el contador de likes de un quiz.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Nuevo conteo de likes o error.</returns>
    Task<Result<int, QuizError>> DecrementLikesAsync(long id);

    /// <summary>
    /// Incrementa el contador de visitas de un quiz.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Nuevo conteo de visitas o error.</returns>
    Task<Result<int, QuizError>> IncrementVisitasAsync(long id);
}
