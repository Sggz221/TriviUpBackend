using TriviUpBackend.Cuestionarios.Entities;

namespace TriviUpBackend.Cuestionarios.Repositories;

/// <summary>
/// Interfaz del repositorio de quizzes.
/// Proporciona métodos para acceder y manipular quizzes en la base de datos.
/// </summary>
public interface IQuizRepository
{
    /// <summary>
    /// Busca un quiz por su ID.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Quiz encontrado o null.</returns>
    Task<Quiz?> FindByIdAsync(long id);

    /// <summary>
    /// Busca un quiz por su código de juego.
    /// </summary>
    /// <param name="gameCode">Código único del quiz.</param>
    /// <returns>Quiz encontrado o null.</returns>
    Task<Quiz?> FindByGameCodeAsync(string gameCode);

    /// <summary>
    /// Busca un quiz por ID incluyendo sus preguntas y respuestas.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Quiz con preguntas cargadas o null.</returns>
    Task<Quiz?> FindByIdWithQuestionsAsync(long id);

    /// <summary>
    /// Busca el quiz al que pertenece una pregunta.
    /// </summary>
    /// <param name="questionId">ID de la pregunta.</param>
    /// <returns>Quiz encontrado o null.</returns>
    Task<Quiz?> FindByQuestionIdAsync(long questionId);

    /// <summary>
    /// Obtiene todos los quizzes de forma paginada.
    /// </summary>
    /// <param name="page">Número de página.</param>
    /// <param name="pageSize">Tamaño de página.</param>
    /// <returns>Lista de quizzes.</returns>
    Task<IEnumerable<Quiz>> FindAllAsync(int page = 1, int pageSize = 10);

    /// <summary>
    /// Obtiene todos los quizzes creados por un usuario.
    /// </summary>
    /// <param name="creatorId">ID del creador.</param>
    /// <returns>Lista de quizzes del creador.</returns>
    Task<IEnumerable<Quiz>> FindByCreatorIdAsync(long creatorId);

    /// <summary>
    /// Busca quizzes públicos con filtros de búsqueda y paginación.
    /// </summary>
    /// <param name="search">Texto de búsqueda (opcional).</param>
    /// <param name="page">Número de página.</param>
    /// <param name="pageSize">Tamaño de página.</param>
    /// <returns>Lista de quizzes públicos.</returns>
    Task<IEnumerable<Quiz>> FindPublicQuizzesAsync(string? search, int page, int pageSize);

    /// <summary>
    /// Cuenta el total de quizzes públicos que coinciden con la búsqueda.
    /// </summary>
    /// <param name="search">Texto de búsqueda (opcional).</param>
    /// <returns>Total de quizzes públicos.</returns>
    Task<int> GetPublicQuizzesCountAsync(string? search);

    /// <summary>
    /// Busca un quiz público por ID.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Quiz público encontrado o null.</returns>
    Task<Quiz?> FindPublicByIdAsync(long id);

    /// <summary>
    /// Incrementa el contador de visitas de un quiz.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Quiz actualizado.</returns>
    Task<Quiz> IncrementVisitasAsync(long id);

    /// <summary>
    /// Incrementa el contador de likes de un quiz.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Quiz actualizado.</returns>
    Task<Quiz> IncrementLikesAsync(long id);

    /// <summary>
    /// Decrementa el contador de likes de un quiz.
    /// </summary>
    /// <param name="id">ID del quiz.</param>
    /// <returns>Quiz actualizado.</returns>
    Task<Quiz> DecrementLikesAsync(long id);

    /// <summary>
    /// Guarda un nuevo quiz en la base de datos.
    /// </summary>
    /// <param name="quiz">Quiz a guardar.</param>
    /// <returns>Quiz guardado con su ID.</returns>
    Task<Quiz> SaveAsync(Quiz quiz);

    /// <summary>
    /// Actualiza un quiz existente.
    /// </summary>
    /// <param name="quiz">Quiz con los datos actualizados.</param>
    /// <returns>Quiz actualizado.</returns>
    Task<Quiz> UpdateAsync(Quiz quiz);

    /// <summary>
    /// Elimina un quiz de la base de datos.
    /// </summary>
    /// <param name="id">ID del quiz a eliminar.</param>
    Task DeleteAsync(long id);

    /// <summary>
    /// Obtiene el total de quizzes en la base de datos.
    /// </summary>
    /// <returns>Total de quizzes.</returns>
    Task<int> GetTotalCountAsync();

    /// <summary>
    /// Obtiene las preguntas de un quiz con sus respuestas.
    /// </summary>
    /// <param name="quizId">ID del quiz.</param>
    /// <returns>Lista de preguntas ordenadas por número.</returns>
    Task<List<Pregunta>> GetQuestionsWithAnswersAsync(long quizId);
}
