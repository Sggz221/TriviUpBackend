using CSharpFunctionalExtensions;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Errors;
using TriviUpBackend.Services.Cache;

namespace TriviUpBackend.Cuestionarios.Services;

/// <summary>
/// Implementación del servicio de quizzes.
/// Gestiona la lógica de negocio para crear, consultar y manipular quizzes.
/// </summary>
public class QuizService(
    IQuizRepository quizRepository,
    ILogger<QuizService> logger,
    ICacheService cacheService
) : IQuizService
{
    private static readonly Random _random = new();
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(5);

    /// <inheritdoc cref="IQuizService.CreateAsync"/>
    public async Task<Result<QuizResponse, QuizError>> CreateAsync(CreateQuizRequest request, long creatorId)
    {
        logger.LogInformation("Creando quiz: {Nombre} por usuario {CreatorId}", request.Nombre, creatorId);

        var validationResult = ValidateCreateRequest(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<QuizResponse, QuizError>(validationResult.Error);
        }

        var gameCode = await GenerateUniqueGameCodeAsync();

        var quiz = new Quiz
        {
            Nombre = request.Nombre,
            GameCode = gameCode,
            CreatorId = creatorId,
            EsPublico = request.EsPublico,
            Preguntas = request.Preguntas.Select(p => new Pregunta
            {
                CreatorId = creatorId,
                NumeroPregunta = p.NumeroPregunta,
                Enunciado = p.Enunciado,
                ImagenUrl = p.ImagenUrl,
                Respuestas = p.Respuestas.Select(r => new Respuesta
                {
                    Texto = r.Texto,
                    EsCorrecta = r.EsCorrecta
                }).ToList()
            }).ToList()
        };

        Quiz savedQuiz;
        try
        {
            savedQuiz = await quizRepository.SaveAsync(quiz);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving quiz for user {CreatorId}", creatorId);
            return Result.Failure<QuizResponse, QuizError>(new QuizValidationError("Error al guardar el quiz"));
        }

        var quizWithQuestions = await quizRepository.FindByIdWithQuestionsAsync(savedQuiz.Id);

        if (quizWithQuestions == null)
        {
            return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError("Quiz no encontrado después de crear"));
        }

        logger.LogInformation("Quiz creado exitosamente con ID: {Id}, GameCode: {GameCode}", savedQuiz.Id, savedQuiz.GameCode);

        return Result.Success<QuizResponse, QuizError>(QuizResponse.FromEntity(quizWithQuestions));
    }

    /// <inheritdoc cref="IQuizService.GetByIdAsync"/>
    public async Task<Result<QuizResponse, QuizError>> GetByIdAsync(long id)
    {
        logger.LogInformation("Obteniendo quiz por ID: {Id}", id);

        var cacheKey = $"quiz:{id}";
        var cached = await cacheService.GetAsync<QuizResponse>(cacheKey);
        if (cached is not null)
        {
            logger.LogDebug("Cache hit for quiz {Id}", id);
            return Result.Success<QuizResponse, QuizError>(cached);
        }

        var quiz = await quizRepository.FindByIdWithQuestionsAsync(id);
        if (quiz == null)
        {
            logger.LogWarning("Quiz no encontrado con ID: {Id}", id);
            return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError($"Quiz con ID {id} no encontrado"));
        }

        var response = QuizResponse.FromEntity(quiz);
        await cacheService.SetAsync(cacheKey, response, DefaultCacheDuration);

        return Result.Success<QuizResponse, QuizError>(response);
    }

    /// <inheritdoc cref="IQuizService.GetByGameCodeAsync"/>
    public async Task<Result<QuizResponse, QuizError>> GetByGameCodeAsync(string gameCode)
    {
        logger.LogInformation("Obteniendo quiz por GameCode: {GameCode}", gameCode);

        var quiz = await quizRepository.FindByGameCodeAsync(gameCode);
        if (quiz == null)
        {
            logger.LogWarning("Quiz no encontrado con GameCode: {GameCode}", gameCode);
            return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError($"Quiz con GameCode {gameCode} no encontrado"));
        }

        var quizWithQuestions = await quizRepository.FindByIdWithQuestionsAsync(quiz.Id);
        if (quizWithQuestions == null)
        {
            return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError($"Quiz con GameCode {gameCode} no encontrado"));
        }

        return Result.Success<QuizResponse, QuizError>(QuizResponse.FromEntity(quizWithQuestions));
    }

    /// <inheritdoc cref="IQuizService.GetAllAsync"/>
    public async Task<Result<(List<QuizResponse> Quizzes, int TotalCount), QuizError>> GetAllAsync(int page = 1, int pageSize = 10)
    {
        logger.LogInformation("Obteniendo lista de quizzes - Página: {Page}, Tamaño: {PageSize}", page, pageSize);

        var quizzes = await quizRepository.FindAllAsync(page, pageSize);
        var totalCount = await quizRepository.GetTotalCountAsync();

        var quizResponses = quizzes.Select(q => new QuizResponse
        {
            Id = q.Id,
            Nombre = q.Nombre,
            GameCode = q.GameCode,
            Preguntas = q.Preguntas.Select(p => new PreguntaResponse
            {
                Id = p.Id,
                NumeroPregunta = p.NumeroPregunta,
                Enunciado = p.Enunciado,
                ImagenUrl = p.ImagenUrl,
                Respuestas = p.Respuestas.Select(r => new RespuestaResponse
                {
                    Id = r.Id,
                    Texto = r.Texto,
                    EsCorrecta = r.EsCorrecta
                }).ToList()
            }).ToList(),
            CreatorId = q.CreatorId,
            FechaCreacion = q.CreatedAt,
            FechaActualizacion = q.UpdatedAt
        }).ToList();

        return Result.Success<(List<QuizResponse>, int), QuizError>((quizResponses, totalCount));
    }

    /// <inheritdoc cref="IQuizService.GetByCreatorIdAsync"/>
    public async Task<Result<List<QuizResponse>, QuizError>> GetByCreatorIdAsync(long creatorId)
    {
        logger.LogInformation("Obteniendo quizzes del usuario: {CreatorId}", creatorId);

        var quizzes = await quizRepository.FindByCreatorIdAsync(creatorId);
        var quizResponses = quizzes.Select(QuizResponse.FromEntity).ToList();

        return Result.Success<List<QuizResponse>, QuizError>(quizResponses);
    }

    /// <inheritdoc cref="IQuizService.UpdateAsync"/>
    public async Task<Result<QuizResponse, QuizError>> UpdateAsync(long id, UpdateQuizRequest request, long userId)
    {
        logger.LogInformation("Actualizando quiz {Id} por usuario {UserId}", id, userId);

        var quiz = await quizRepository.FindByIdWithQuestionsAsync(id);
        if (quiz == null)
        {
            return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError($"Quiz con ID {id} no encontrado"));
        }

        if (quiz.CreatorId != userId)
        {
            logger.LogWarning("Usuario {UserId} intentó actualizar quiz {Id} creado por {CreatorId}", userId, id, quiz.CreatorId);
            return Result.Failure<QuizResponse, QuizError>(new QuizForbiddenError("No tienes permiso para modificar este quiz"));
        }

        var validationResult = ValidateUpdateRequest(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<QuizResponse, QuizError>(validationResult.Error);
        }

        quiz.Nombre = request.Nombre;

        // Quitar preguntas y respuestas antiguas
        quiz.Preguntas.Clear();

        // Añadir nuevas preguntas
        foreach (var preguntaRequest in request.Preguntas)
        {
            var pregunta = new Pregunta
            {
                QuizId = quiz.Id,
                CreatorId = quiz.CreatorId,
                NumeroPregunta = preguntaRequest.NumeroPregunta,
                Enunciado = preguntaRequest.Enunciado,
                ImagenUrl = preguntaRequest.ImagenUrl,
                Respuestas = preguntaRequest.Respuestas.Select(r => new Respuesta
                {
                    Texto = r.Texto,
                    EsCorrecta = r.EsCorrecta
                }).ToList()
            };
            quiz.Preguntas.Add(pregunta);
        }

        await quizRepository.UpdateAsync(quiz);

        var updatedQuiz = await quizRepository.FindByIdWithQuestionsAsync(id);
        if (updatedQuiz == null)
        {
            return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError($"Quiz con ID {id} no encontrado después de actualizar"));
        }

        // Invalidate individual quiz cache
        await cacheService.RemoveAsync($"quiz:{id}");

        logger.LogInformation("Quiz {Id} actualizado exitosamente", id);

        return Result.Success<QuizResponse, QuizError>(QuizResponse.FromEntity(updatedQuiz));
    }

    /// <inheritdoc cref="IQuizService.DeleteAsync"/>
    public async Task<UnitResult<QuizError>> DeleteAsync(long id, long userId)
    {
        logger.LogInformation("Eliminando quiz {Id} por usuario {UserId}", id, userId);

        var quiz = await quizRepository.FindByIdAsync(id);
        if (quiz == null)
        {
            return UnitResult.Failure<QuizError>(new QuizNotFoundError($"Quiz con ID {id} no encontrado"));
        }

        if (quiz.CreatorId != userId)
        {
            logger.LogWarning("Usuario {UserId} intentó eliminar quiz {Id} creado por {CreatorId}", userId, id, quiz.CreatorId);
            return UnitResult.Failure<QuizError>(new QuizForbiddenError("No tienes permiso para eliminar este quiz"));
        }

        await quizRepository.DeleteAsync(id);

        // Invalidate individual quiz cache
        await cacheService.RemoveAsync($"quiz:{id}");

        logger.LogInformation("Quiz {Id} eliminado exitosamente", id);

        return UnitResult.Success<QuizError>();
    }

    /// <inheritdoc cref="IQuizService.GetPublicQuizzesAsync"/>
    public async Task<Result<(List<PublicQuizResponse> Quizzes, int TotalCount), QuizError>> GetPublicQuizzesAsync(string? search, int page, int pageSize)
    {
        logger.LogInformation("[QuizService] Obteniendo quizzes públicos sin cache - Search: {Search}, Page: {Page}, PageSize: {PageSize}",
            search, page, pageSize);

        // Sin cache - siempre obtener de la base de datos directamente
        var quizzes = await quizRepository.FindPublicQuizzesAsync(search, page, pageSize);
        var totalCount = await quizRepository.GetPublicQuizzesCountAsync(search);

        var publicQuizResponses = quizzes.Select(PublicQuizResponse.FromEntity).ToList();
        var result = (publicQuizResponses, totalCount);

        logger.LogInformation("[QuizService] Se encontraron {Count} quizzes públicos de {Total} total", publicQuizResponses.Count, totalCount);

        return Result.Success<(List<PublicQuizResponse>, int), QuizError>(result);
    }

    /// <inheritdoc cref="IQuizService.IncrementLikesAsync"/>
    public async Task<Result<int, QuizError>> IncrementLikesAsync(long id)
    {
        logger.LogInformation("[QuizService] Incrementando likes del quiz {Id}", id);

        var quiz = await quizRepository.FindByIdAsync(id);
        if (quiz == null)
        {
            logger.LogWarning("[QuizService] Quiz no encontrado con ID: {Id}", id);
            return Result.Failure<int, QuizError>(new QuizNotFoundError($"Quiz con ID {id} no encontrado"));
        }

        logger.LogInformation("[QuizService] Likes actuales del quiz {Id}: {Likes}", id, quiz.Likes);

        var updatedQuiz = await quizRepository.IncrementLikesAsync(id);

        logger.LogInformation("[QuizService] Likes del quiz {Id} incrementados a: {Likes}", id, updatedQuiz.Likes);

        return Result.Success<int, QuizError>(updatedQuiz.Likes);
    }

    /// <inheritdoc cref="IQuizService.DecrementLikesAsync"/>
    public async Task<Result<int, QuizError>> DecrementLikesAsync(long id)
    {
        logger.LogInformation("[QuizService] Decrementando likes del quiz {Id}", id);

        var quiz = await quizRepository.FindByIdAsync(id);
        if (quiz == null)
        {
            logger.LogWarning("[QuizService] Quiz no encontrado con ID: {Id}", id);
            return Result.Failure<int, QuizError>(new QuizNotFoundError($"Quiz con ID {id} no encontrado"));
        }

        logger.LogInformation("[QuizService] Likes actuales del quiz {Id}: {Likes}", id, quiz.Likes);

        var updatedQuiz = await quizRepository.DecrementLikesAsync(id);

        logger.LogInformation("[QuizService] Likes del quiz {Id} decrementados a: {Likes}", id, updatedQuiz.Likes);

        return Result.Success<int, QuizError>(updatedQuiz.Likes);
    }

    /// <inheritdoc cref="IQuizService.IncrementVisitasAsync"/>
    public async Task<Result<int, QuizError>> IncrementVisitasAsync(long id)
    {
        logger.LogInformation("[QuizService] Incrementando visitas del quiz {Id}", id);

        var quiz = await quizRepository.FindByIdAsync(id);
        if (quiz == null)
        {
            logger.LogWarning("[QuizService] Quiz no encontrado con ID: {Id}", id);
            return Result.Failure<int, QuizError>(new QuizNotFoundError($"Quiz con ID {id} no encontrado"));
        }

        logger.LogInformation("[QuizService] Visitas actuales del quiz {Id}: {Visitas}", id, quiz.Visitas);

        var updatedQuiz = await quizRepository.IncrementVisitasAsync(id);

        logger.LogInformation("[QuizService] Visitas del quiz {Id} incrementadas a: {Visitas}", id, updatedQuiz.Visitas);

        return Result.Success<int, QuizError>(updatedQuiz.Visitas);
    }

    private UnitResult<QuizError> ValidateCreateRequest(CreateQuizRequest request)
    {
        if (request.Preguntas == null || request.Preguntas.Count == 0)
        {
            return UnitResult.Failure<QuizError>(new QuizValidationError("Debe haber al menos una pregunta"));
        }

        for (int i = 0; i < request.Preguntas.Count; i++)
        {
            var pregunta = request.Preguntas[i];

            if (pregunta.Respuestas == null || pregunta.Respuestas.Count < 2)
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} debe tener al menos 2 respuestas"));
            }

            var correctCount = pregunta.Respuestas.Count(r => r.EsCorrecta);
            if (correctCount != 1)
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} debe tener exactamente una respuesta correcta"));
            }
        }

        return UnitResult.Success<QuizError>();
    }

    private UnitResult<QuizError> ValidateUpdateRequest(UpdateQuizRequest request)
    {
        if (request.Preguntas == null || request.Preguntas.Count == 0)
        {
            return UnitResult.Failure<QuizError>(new QuizValidationError("Debe haber al menos una pregunta"));
        }

        for (int i = 0; i < request.Preguntas.Count; i++)
        {
            var pregunta = request.Preguntas[i];

            if (pregunta.Respuestas == null || pregunta.Respuestas.Count < 2)
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} debe tener al menos 2 respuestas"));
            }

            var correctCount = pregunta.Respuestas.Count(r => r.EsCorrecta);
            if (correctCount != 1)
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} debe tener exactamente una respuesta correcta"));
            }
        }

        return UnitResult.Success<QuizError>();
    }

    private async Task<string> GenerateUniqueGameCodeAsync()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string gameCode;

        do
        {
            gameCode = new string(Enumerable.Range(0, 6)
                .Select(_ => chars[_random.Next(chars.Length)])
                .ToArray());
        }
        while (await quizRepository.FindByGameCodeAsync(gameCode) != null);

        return gameCode;
    }
}
