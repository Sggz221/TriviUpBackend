using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Database;

namespace TriviUpBackend.Cuestionarios.Repositories;

/// <summary>
/// Implementación del repositorio de quizzes.
/// Gestiona el acceso a la base de datos para entidades Quiz.
/// </summary>
public class QuizRepository(
    Context context,
    ILogger<QuizRepository> logger
) : IQuizRepository
{
    /// <summary>
    /// Busca un quiz por su ID.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.FindByIdAsync"/>
    public async Task<Quiz?> FindByIdAsync(long id)
    {
        return await context.Quizzes.FindAsync(id);
    }

    /// <summary>
    /// Busca un quiz por su código de juego.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.FindByGameCodeAsync"/>
    public async Task<Quiz?> FindByGameCodeAsync(string gameCode)
    {
        return await context.Quizzes
            .FirstOrDefaultAsync(q => q.GameCode == gameCode);
    }

    /// <summary>
    /// Busca un quiz por ID incluyendo sus preguntas y respuestas.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.FindByIdWithQuestionsAsync"/>
    public async Task<Quiz?> FindByIdWithQuestionsAsync(long id)
    {
        return await context.Quizzes
            .Include(q => q.Preguntas)
                .ThenInclude(p => p.Respuestas)
            .FirstOrDefaultAsync(q => q.Id == id);
    }

    /// <summary>
    /// Busca el quiz al que pertenece una pregunta.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.FindByQuestionIdAsync"/>
    public async Task<Quiz?> FindByQuestionIdAsync(long questionId)
    {
        return await context.Quizzes
            .Include(q => q.Preguntas)
                .ThenInclude(p => p.Respuestas)
            .FirstOrDefaultAsync(q => q.Preguntas.Any(p => p.Id == questionId));
    }

    /// <summary>
    /// Obtiene todos los quizzes de forma paginada.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.FindAllAsync"/>
    public async Task<IEnumerable<Quiz>> FindAllAsync(int page = 1, int pageSize = 10)
    {
        return await context.Quizzes
            .Include(q => q.Preguntas)
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// Obtiene todos los quizzes creados por un usuario.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.FindByCreatorIdAsync"/>
    public async Task<IEnumerable<Quiz>> FindByCreatorIdAsync(long creatorId)
    {
        return await context.Quizzes
            .Include(q => q.Preguntas)
            .Where(q => q.CreatorId == creatorId)
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Busca quizzes públicos con filtros de búsqueda y paginación.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.FindPublicQuizzesAsync"/>
    public async Task<IEnumerable<Quiz>> FindPublicQuizzesAsync(string? search, int page, int pageSize)
    {
        var query = context.Quizzes
            .Include(q => q.Creator)
            .Include(q => q.Preguntas)
            .Where(q => q.EsPublico);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTrimmed = search.Trim();
            query = query.Where(q => q.Nombre.Contains(searchTrimmed));
        }

        return await query
            .OrderByDescending(q => q.Visitas)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// Cuenta el total de quizzes públicos que coinciden con la búsqueda.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.GetPublicQuizzesCountAsync"/>
    public async Task<int> GetPublicQuizzesCountAsync(string? search)
    {
        var query = context.Quizzes.Where(q => q.EsPublico);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTrimmed = search.Trim();
            query = query.Where(q => q.Nombre.Contains(searchTrimmed));
        }

        return await query.CountAsync();
    }

    /// <summary>
    /// Busca un quiz público por ID.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.FindPublicByIdAsync"/>
    public async Task<Quiz?> FindPublicByIdAsync(long id)
    {
        return await context.Quizzes
            .Include(q => q.Creator)
            .Include(q => q.Preguntas)
            .FirstOrDefaultAsync(q => q.Id == id && q.EsPublico);
    }

    /// <summary>
    /// Incrementa el contador de visitas de un quiz.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.IncrementVisitasAsync"/>
    public async Task<Quiz> IncrementVisitasAsync(long id)
    {
        var quiz = await FindByIdAsync(id);
        if (quiz == null)
        {
            throw new InvalidOperationException($"Quiz with ID {id} not found");
        }

        quiz.Visitas++;
        await context.SaveChangesAsync();
        logger.LogInformation("Quiz {Id} visitas incremented to {Visitas}", id, quiz.Visitas);
        return quiz;
    }

    /// <summary>
    /// Incrementa el contador de likes de un quiz.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.IncrementLikesAsync"/>
    public async Task<Quiz> IncrementLikesAsync(long id)
    {
        var quiz = await FindByIdAsync(id);
        if (quiz == null)
        {
            throw new InvalidOperationException($"Quiz with ID {id} not found");
        }

        quiz.Likes++;
        await context.SaveChangesAsync();
        logger.LogInformation("Quiz {Id} likes incremented to {Likes}", id, quiz.Likes);
        return quiz;
    }

    /// <summary>
    /// Decrementa el contador de likes de un quiz.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.DecrementLikesAsync"/>
    public async Task<Quiz> DecrementLikesAsync(long id)
    {
        var quiz = await FindByIdAsync(id);
        if (quiz == null)
        {
            throw new InvalidOperationException($"Quiz with ID {id} not found");
        }

        if (quiz.Likes > 0)
        {
            quiz.Likes--;
        }
        await context.SaveChangesAsync();
        logger.LogInformation("Quiz {Id} likes decremented to {Likes}", id, quiz.Likes);
        return quiz;
    }

    /// <summary>
    /// Guarda un nuevo quiz en la base de datos.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.SaveAsync"/>
    public async Task<Quiz> SaveAsync(Quiz quiz)
    {
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        logger.LogInformation("Quiz creado con ID: {Id}, GameCode: {GameCode}", quiz.Id, quiz.GameCode);
        return quiz;
    }

    /// <summary>
    /// Actualiza un quiz existente.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.UpdateAsync"/>
    public async Task<Quiz> UpdateAsync(Quiz quiz)
    {
        context.Quizzes.Update(quiz);
        await context.SaveChangesAsync();
        logger.LogInformation("Quiz actualizado con ID: {Id}", quiz.Id);
        return quiz;
    }

    /// <summary>
    /// Elimina un quiz de la base de datos.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.DeleteAsync"/>
    public async Task DeleteAsync(long id)
    {
        var quiz = await FindByIdAsync(id);
        if (quiz is not null)
        {
            context.Quizzes.Remove(quiz);
            await context.SaveChangesAsync();
            logger.LogInformation("Quiz eliminado con ID: {Id}", id);
        }
    }

    /// <summary>
    /// Obtiene el total de quizzes en la base de datos.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.GetTotalCountAsync"/>
    public async Task<int> GetTotalCountAsync()
    {
        return await context.Quizzes.CountAsync();
    }

    /// <summary>
    /// Obtiene las preguntas de un quiz con sus respuestas.
    /// </summary>
    /// <inheritdoc cref="IQuizRepository.GetQuestionsWithAnswersAsync"/>
    public async Task<List<Pregunta>> GetQuestionsWithAnswersAsync(long quizId)
    {
        return await context.Preguntas
            .Where(p => p.QuizId == quizId)
            .Include(p => p.Respuestas)
            .OrderBy(p => p.NumeroPregunta)
            .ToListAsync();
    }
}
