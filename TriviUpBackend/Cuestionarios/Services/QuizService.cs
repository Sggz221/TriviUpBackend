using System.Text.Json;
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
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(5);

    /// <inheritdoc cref="IQuizService.CreateAsync"/>
    public async Task<Result<QuizResponse, QuizError>> CreateAsync(CreateQuizRequest request, long creatorId)
    {
        logger.LogInformation("Creando quiz: {Nombre} por usuario {CreatorId}", request.Nombre, creatorId);

        var validationResult = request.EsBorrador
            ? UnitResult.Success<QuizError>()
            : ValidateCreateRequest(request);
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
            EsBorrador = request.EsBorrador,
            VersionPublicada = request.EsBorrador ? 0 : 1,
            Preguntas = request.Preguntas.Select(p => new Pregunta
            {
                CreatorId = creatorId,
                NumeroPregunta = p.NumeroPregunta,
                Enunciado = p.Enunciado,
                ImagenUrl = p.ImagenUrl,
                FaseNumero = p.FaseNumero,
                FaseNombre = NormalizeFaseNombre(p.FaseNombre),
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
        if (quiz == null || quiz.EsBorrador)
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
                FaseNumero = p.FaseNumero,
                FaseNombre = p.FaseNombre,
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
        var conBorrador = await quizRepository.FindQuizIdsWithDraftAsync(creatorId);
        var quizResponses = quizzes
            .Select(q => QuizResponse.FromEntity(q) with { TieneBorrador = conBorrador.Contains(q.Id) })
            .ToList();

        return Result.Success<List<QuizResponse>, QuizError>(quizResponses);
    }

    /// <inheritdoc cref="IQuizService.UpdateAsync"/>
    public async Task<Result<QuizResponse, QuizError>> UpdateAsync(long id, UpdateQuizRequest request, long userId)
    {
        logger.LogInformation("Actualizando quiz {Id} por usuario {UserId} (borrador: {Borrador})", id, userId, request.EsBorrador);

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

        var yaPublicado = quiz.VersionPublicada > 0;

        if (request.EsBorrador)
        {
            // Quiz ya publicado: el borrador va aparte y lo publicado no se toca
            if (yaPublicado)
            {
                return await SaveDraftRowAsync(quiz, request);
            }

            // Nunca publicado: el borrador vive en las tablas vivas
            ApplyContent(quiz, request);
            quiz.EsBorrador = true;
            if (request.EsPublico.HasValue)
            {
                quiz.EsPublico = request.EsPublico.Value;
            }
            await quizRepository.UpdateAsync(quiz);
            await cacheService.RemoveAsync($"quiz:{id}");
            return await ReloadAsync(id);
        }

        var validationResult = ValidateUpdateRequest(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<QuizResponse, QuizError>(validationResult.Error);
        }

        if (yaPublicado)
        {
            // Nueva versión: se archiva la actual y se descarta el borrador pendiente
            var archived = new QuizVersion
            {
                QuizId = quiz.Id,
                Numero = quiz.VersionPublicada,
                Estado = QuizVersionEstado.Archivada,
                Nombre = quiz.Nombre,
                EsPublico = quiz.EsPublico,
                Contenido = JsonSerializer.Serialize(ToRequest(quiz), JsonOpts),
                PublishedAt = quiz.UpdatedAt
            };
            var draft = await quizRepository.FindDraftAsync(quiz.Id);

            ApplyContent(quiz, request);
            if (request.EsPublico.HasValue)
            {
                quiz.EsPublico = request.EsPublico.Value;
            }
            quiz.VersionPublicada++;

            await quizRepository.PublishVersionAsync(quiz, archived, draft);
        }
        else
        {
            ApplyContent(quiz, request);
            if (request.EsPublico.HasValue)
            {
                quiz.EsPublico = request.EsPublico.Value;
            }
            quiz.EsBorrador = false;
            quiz.VersionPublicada = 1;
            await quizRepository.UpdateAsync(quiz);
        }

        // Invalidate individual quiz cache
        await cacheService.RemoveAsync($"quiz:{id}");

        logger.LogInformation("Quiz {Id} publicado (versión {Version})", id, quiz.VersionPublicada);

        return await ReloadAsync(id);
    }

    /// <inheritdoc cref="IQuizService.SaveDraftAsync"/>
    public Task<Result<QuizResponse, QuizError>> SaveDraftAsync(long id, UpdateQuizRequest request, long userId)
        => UpdateAsync(id, request with { EsBorrador = true }, userId);

    /// <inheritdoc cref="IQuizService.GetDraftAsync"/>
    public async Task<Result<QuizResponse, QuizError>> GetDraftAsync(long id, long userId)
    {
        var quiz = await quizRepository.FindByIdWithQuestionsAsync(id);
        var check = CheckOwner(quiz, id, userId);
        if (check is not null) return Result.Failure<QuizResponse, QuizError>(check);

        // Nunca publicado: el propio quiz es el borrador
        if (quiz!.VersionPublicada == 0)
        {
            return Result.Success<QuizResponse, QuizError>(QuizResponse.FromEntity(quiz));
        }

        var draft = await quizRepository.FindDraftAsync(id);
        if (draft is null)
        {
            return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError("Este quiz no tiene borrador pendiente"));
        }

        var contenido = JsonSerializer.Deserialize<UpdateQuizRequest>(draft.Contenido, JsonOpts)
                        ?? new UpdateQuizRequest { Nombre = draft.Nombre };
        return Result.Success<QuizResponse, QuizError>(QuizResponse.FromDraft(quiz, contenido, draft.CreatedAt));
    }

    /// <inheritdoc cref="IQuizService.PublishAsync"/>
    public async Task<Result<QuizResponse, QuizError>> PublishAsync(long id, long userId)
    {
        var quiz = await quizRepository.FindByIdWithQuestionsAsync(id);
        var check = CheckOwner(quiz, id, userId);
        if (check is not null) return Result.Failure<QuizResponse, QuizError>(check);

        UpdateQuizRequest contenido;
        if (quiz!.VersionPublicada == 0)
        {
            contenido = ToRequest(quiz);
        }
        else
        {
            var draft = await quizRepository.FindDraftAsync(id);
            if (draft is null)
            {
                return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError("No hay borrador pendiente que publicar"));
            }
            contenido = JsonSerializer.Deserialize<UpdateQuizRequest>(draft.Contenido, JsonOpts)
                        ?? new UpdateQuizRequest { Nombre = draft.Nombre };
        }

        return await UpdateAsync(id, contenido with { EsBorrador = false }, userId);
    }

    /// <inheritdoc cref="IQuizService.DiscardDraftAsync"/>
    public async Task<UnitResult<QuizError>> DiscardDraftAsync(long id, long userId)
    {
        var quiz = await quizRepository.FindByIdAsync(id);
        var check = CheckOwner(quiz, id, userId);
        if (check is not null) return UnitResult.Failure<QuizError>(check);

        var draft = await quizRepository.FindDraftAsync(id);
        if (draft is null)
        {
            return UnitResult.Failure<QuizError>(new QuizNotFoundError("Este quiz no tiene borrador pendiente"));
        }

        await quizRepository.DeleteDraftAsync(draft);
        return UnitResult.Success<QuizError>();
    }

    /// <inheritdoc cref="IQuizService.GetVersionsAsync"/>
    public async Task<Result<List<QuizVersionResponse>, QuizError>> GetVersionsAsync(long id, long userId)
    {
        var quiz = await quizRepository.FindByIdAsync(id);
        var check = CheckOwner(quiz, id, userId);
        if (check is not null) return Result.Failure<List<QuizVersionResponse>, QuizError>(check);

        var versiones = new List<QuizVersionResponse>();

        var draft = await quizRepository.FindDraftAsync(id);
        if (quiz!.VersionPublicada == 0)
        {
            versiones.Add(new QuizVersionResponse { Estado = "Borrador", Nombre = quiz.Nombre, Fecha = quiz.UpdatedAt });
        }
        else
        {
            if (draft is not null)
            {
                versiones.Add(new QuizVersionResponse { Estado = "Borrador", Nombre = draft.Nombre, Fecha = draft.CreatedAt });
            }
            versiones.Add(new QuizVersionResponse
            {
                Numero = quiz.VersionPublicada,
                Estado = "Publicada",
                Nombre = quiz.Nombre,
                Fecha = quiz.UpdatedAt
            });
        }

        foreach (var v in await quizRepository.FindArchivedAsync(id))
        {
            versiones.Add(new QuizVersionResponse
            {
                Numero = v.Numero,
                Estado = "Archivada",
                Nombre = v.Nombre,
                Fecha = v.PublishedAt ?? v.CreatedAt
            });
        }

        return Result.Success<List<QuizVersionResponse>, QuizError>(versiones);
    }

    /// <inheritdoc cref="IQuizService.RestoreVersionAsync"/>
    public async Task<Result<QuizResponse, QuizError>> RestoreVersionAsync(long id, int numero, long userId)
    {
        var quiz = await quizRepository.FindByIdWithQuestionsAsync(id);
        var check = CheckOwner(quiz, id, userId);
        if (check is not null) return Result.Failure<QuizResponse, QuizError>(check);

        if (quiz!.VersionPublicada == 0)
        {
            return Result.Failure<QuizResponse, QuizError>(new QuizValidationError("El quiz aún no tiene versiones publicadas"));
        }

        UpdateQuizRequest contenido;
        if (numero == quiz.VersionPublicada)
        {
            contenido = ToRequest(quiz);
        }
        else
        {
            var archivada = await quizRepository.FindArchivedAsync(id, numero);
            if (archivada is null)
            {
                return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError($"Versión {numero} no encontrada"));
            }
            contenido = JsonSerializer.Deserialize<UpdateQuizRequest>(archivada.Contenido, JsonOpts)
                        ?? new UpdateQuizRequest { Nombre = archivada.Nombre };
        }

        var guardado = await SaveDraftRowAsync(quiz, contenido with { EsBorrador = true });
        if (guardado.IsFailure) return guardado;

        var draft = await quizRepository.FindDraftAsync(id);
        return Result.Success<QuizResponse, QuizError>(
            QuizResponse.FromDraft(quiz, contenido, draft?.CreatedAt ?? DateTime.UtcNow));
    }

    // ---------- helpers de versiones ----------

    private async Task<Result<QuizResponse, QuizError>> SaveDraftRowAsync(Quiz quiz, UpdateQuizRequest request)
    {
        var draft = await quizRepository.FindDraftAsync(quiz.Id) ?? new QuizVersion
        {
            QuizId = quiz.Id,
            Estado = QuizVersionEstado.Borrador
        };

        draft.Nombre = request.Nombre;
        draft.EsPublico = request.EsPublico ?? quiz.EsPublico;
        draft.Contenido = JsonSerializer.Serialize(request with { EsBorrador = false }, JsonOpts);
        draft.CreatedAt = DateTime.UtcNow;

        await quizRepository.SaveDraftAsync(draft);

        return Result.Success<QuizResponse, QuizError>(
            QuizResponse.FromEntity(quiz) with { TieneBorrador = true });
    }

    private async Task<Result<QuizResponse, QuizError>> ReloadAsync(long id)
    {
        var updated = await quizRepository.FindByIdWithQuestionsAsync(id);
        if (updated == null)
        {
            return Result.Failure<QuizResponse, QuizError>(new QuizNotFoundError($"Quiz con ID {id} no encontrado después de actualizar"));
        }
        return Result.Success<QuizResponse, QuizError>(QuizResponse.FromEntity(updated));
    }

    private static QuizError? CheckOwner(Quiz? quiz, long id, long userId)
    {
        if (quiz is null) return new QuizNotFoundError($"Quiz con ID {id} no encontrado");
        if (quiz.CreatorId != userId) return new QuizForbiddenError("No tienes permiso para acceder a este quiz");
        return null;
    }

    /// <summary>Sustituye el contenido vivo del quiz (nombre, preguntas y respuestas).</summary>
    private static void ApplyContent(Quiz quiz, UpdateQuizRequest request)
    {
        quiz.Nombre = request.Nombre;

        // Quitar preguntas y respuestas antiguas
        quiz.Preguntas.Clear();

        // Añadir nuevas preguntas
        foreach (var preguntaRequest in request.Preguntas)
        {
            quiz.Preguntas.Add(new Pregunta
            {
                QuizId = quiz.Id,
                CreatorId = quiz.CreatorId,
                NumeroPregunta = preguntaRequest.NumeroPregunta,
                Enunciado = preguntaRequest.Enunciado,
                ImagenUrl = preguntaRequest.ImagenUrl,
                FaseNumero = preguntaRequest.FaseNumero,
                FaseNombre = NormalizeFaseNombre(preguntaRequest.FaseNombre),
                Respuestas = preguntaRequest.Respuestas.Select(r => new Respuesta
                {
                    Texto = r.Texto,
                    EsCorrecta = r.EsCorrecta
                }).ToList()
            });
        }
    }

    /// <summary>Convierte el contenido vivo del quiz al formato de contenido versionado.</summary>
    private static UpdateQuizRequest ToRequest(Quiz quiz) => new()
    {
        Nombre = quiz.Nombre,
        EsPublico = quiz.EsPublico,
        Preguntas = quiz.Preguntas
            .OrderBy(p => p.NumeroPregunta)
            .Select(p => new UpdatePreguntaRequest
            {
                NumeroPregunta = p.NumeroPregunta,
                Enunciado = p.Enunciado,
                ImagenUrl = p.ImagenUrl,
                FaseNumero = p.FaseNumero,
                FaseNombre = p.FaseNombre,
                Respuestas = p.Respuestas
                    .Select(r => new UpdateRespuestaRequest { Texto = r.Texto, EsCorrecta = r.EsCorrecta })
                    .ToList()
            }).ToList()
    };

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

            if (string.IsNullOrWhiteSpace(pregunta.Enunciado))
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} no puede estar vacía"));
            }

            if (pregunta.Respuestas == null || pregunta.Respuestas.Count < 2 ||
                pregunta.Respuestas.Any(r => string.IsNullOrWhiteSpace(r.Texto)))
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} debe tener al menos 2 respuestas con texto"));
            }

            var correctCount = pregunta.Respuestas.Count(r => r.EsCorrecta);
            if (correctCount != 1)
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} debe tener exactamente una respuesta correcta"));
            }
        }

        return ValidateFases(request.Preguntas.Select(p => (p.NumeroPregunta, p.FaseNumero, p.FaseNombre)));
    }

    private static string? NormalizeFaseNombre(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim();

    /// <summary>
    /// Las fases, siguiendo el orden de las preguntas, deben empezar en 1, no retroceder,
    /// avanzar de una en una y mantener el mismo nombre dentro de cada fase.
    /// </summary>
    private static UnitResult<QuizError> ValidateFases(IEnumerable<(int Numero, int Fase, string? Nombre)> preguntas)
    {
        var fase = 1;
        string? nombreFase = null;
        var primera = true;

        foreach (var (_, faseActual, nombreRaw) in preguntas.OrderBy(p => p.Numero))
        {
            var nombre = NormalizeFaseNombre(nombreRaw);

            if (primera)
            {
                if (faseActual != 1)
                {
                    return UnitResult.Failure<QuizError>(new QuizValidationError("Las fases deben empezar en 1"));
                }
                nombreFase = nombre;
                primera = false;
                continue;
            }

            if (faseActual == fase)
            {
                if (!string.Equals(nombre, nombreFase, StringComparison.Ordinal))
                {
                    return UnitResult.Failure<QuizError>(
                        new QuizValidationError($"Las preguntas de la fase {fase} deben tener el mismo nombre de fase"));
                }
            }
            else if (faseActual == fase + 1)
            {
                fase = faseActual;
                nombreFase = nombre;
            }
            else
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError("Las fases deben ser consecutivas y no puede haber fases vacías"));
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

            if (string.IsNullOrWhiteSpace(pregunta.Enunciado))
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} no puede estar vacía"));
            }

            if (pregunta.Respuestas == null || pregunta.Respuestas.Count < 2 ||
                pregunta.Respuestas.Any(r => string.IsNullOrWhiteSpace(r.Texto)))
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} debe tener al menos 2 respuestas con texto"));
            }

            var correctCount = pregunta.Respuestas.Count(r => r.EsCorrecta);
            if (correctCount != 1)
            {
                return UnitResult.Failure<QuizError>(
                    new QuizValidationError($"La pregunta {i + 1} debe tener exactamente una respuesta correcta"));
            }
        }

        return ValidateFases(request.Preguntas.Select(p => (p.NumeroPregunta, p.FaseNumero, p.FaseNombre)));
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
