using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using TriviUpBackend.Cuestionarios.Services;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Services.Cache;
using TriviUpBackend.Errors;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;

namespace TriviUpTest.Services;

public class QuizServiceTests
{
    private readonly Mock<IQuizRepository> _mockRepo;
    private readonly Mock<ICacheService> _mockCache;
    private readonly Mock<ILogger<QuizService>> _mockLogger;
    private readonly QuizService _service;

    public QuizServiceTests()
    {
        _mockRepo = new Mock<IQuizRepository>();
        _mockCache = new Mock<ICacheService>();
        _mockLogger = new Mock<ILogger<QuizService>>();
        _service = new QuizService(_mockRepo.Object, _mockLogger.Object, _mockCache.Object);
    }

    // ========== GetByIdAsync Tests ==========

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsQuiz()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Test Quiz");
        _mockCache.Setup(c => c.GetAsync<QuizResponse>(It.IsAny<string>())).ReturnsAsync((QuizResponse?)null);
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(quizId)).ReturnsAsync(quiz);

        // Act
        var result = await _service.GetByIdAsync(quizId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(quizId, result.Value.Id);
        Assert.Equal("Test Quiz", result.Value.Nombre);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingId_ReturnsNotFoundError()
    {
        // Arrange
        var quizId = 999L;
        _mockCache.Setup(c => c.GetAsync<QuizResponse>(It.IsAny<string>())).ReturnsAsync((QuizResponse?)null);
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(quizId)).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.GetByIdAsync(quizId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    [Fact]
    public async Task GetByIdAsync_CachedQuiz_ReturnsCachedWithoutDbCall()
    {
        // Arrange
        var quizId = 1L;
        var cachedResponse = new QuizResponse { Id = quizId, Nombre = "Cached Quiz" };
        _mockCache.Setup(c => c.GetAsync<QuizResponse>($"quiz:{quizId}")).ReturnsAsync(cachedResponse);

        // Act
        var result = await _service.GetByIdAsync(quizId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Cached Quiz", result.Value.Nombre);
        _mockRepo.Verify(r => r.FindByIdWithQuestionsAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_CacheMiss_QueriesDatabaseAndCaches()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "DB Quiz");
        _mockCache.Setup(c => c.GetAsync<QuizResponse>($"quiz:{quizId}")).ReturnsAsync((QuizResponse?)null);
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(quizId)).ReturnsAsync(quiz);

        // Act
        var result = await _service.GetByIdAsync(quizId);

        // Assert
        Assert.True(result.IsSuccess);
        _mockCache.Verify(c => c.SetAsync($"quiz:{quizId}", It.IsAny<QuizResponse>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    // ========== GetByGameCodeAsync Tests ==========

    [Fact]
    public async Task GetByGameCodeAsync_ExistingCode_ReturnsQuiz()
    {
        // Arrange
        var gameCode = "ABC123";
        var quiz = CreateSampleQuiz(1L, "Test Quiz", gameCode);
        _mockRepo.Setup(r => r.FindByGameCodeAsync(gameCode)).ReturnsAsync(quiz);
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(1L)).ReturnsAsync(quiz);

        // Act
        var result = await _service.GetByGameCodeAsync(gameCode);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(gameCode, result.Value.GameCode);
    }

    [Fact]
    public async Task GetByGameCodeAsync_NonExistingCode_ReturnsNotFoundError()
    {
        // Arrange
        var gameCode = "NONEXIST";
        _mockRepo.Setup(r => r.FindByGameCodeAsync(gameCode)).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.GetByGameCodeAsync(gameCode);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    [Fact]
    public async Task GetByGameCodeAsync_QuizFoundButWithQuestionsNull_ReturnsNotFoundError()
    {
        // Arrange
        var gameCode = "ABC123";
        var quiz = CreateSampleQuiz(1L, "Test Quiz", gameCode);
        _mockRepo.Setup(r => r.FindByGameCodeAsync(gameCode)).ReturnsAsync(quiz);
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(1L)).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.GetByGameCodeAsync(gameCode);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    // ========== GetAllAsync Tests ==========

    [Fact]
    public async Task GetAllAsync_WithQuizzes_ReturnsQuizzesAndTotalCount()
    {
        // Arrange
        var quizzes = new List<Quiz>
        {
            CreateSampleQuiz(1, "Quiz 1"),
            CreateSampleQuiz(2, "Quiz 2")
        };
        _mockRepo.Setup(r => r.FindAllAsync(1, 10)).ReturnsAsync(quizzes);
        _mockRepo.Setup(r => r.GetTotalCountAsync()).ReturnsAsync(2);

        // Act
        var result = await _service.GetAllAsync(1, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Quizzes.Count);
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetAllAsync_EmptyResults_ReturnsEmptyList()
    {
        // Arrange
        _mockRepo.Setup(r => r.FindAllAsync(1, 10)).ReturnsAsync(new List<Quiz>());
        _mockRepo.Setup(r => r.GetTotalCountAsync()).ReturnsAsync(0);

        // Act
        var result = await _service.GetAllAsync(1, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Quizzes);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetAllAsync_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        var quizzes = new List<Quiz> { CreateSampleQuiz(3, "Quiz 3") };
        _mockRepo.Setup(r => r.FindAllAsync(2, 2)).ReturnsAsync(quizzes);
        _mockRepo.Setup(r => r.GetTotalCountAsync()).ReturnsAsync(5);

        // Act
        var result = await _service.GetAllAsync(2, 2);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Quizzes);
        Assert.Equal(5, result.Value.TotalCount);
    }

    // ========== GetByCreatorIdAsync Tests ==========

    [Fact]
    public async Task GetByCreatorIdAsync_WithQuizzes_ReturnsQuizzes()
    {
        // Arrange
        var creatorId = 1L;
        var quizzes = new List<Quiz>
        {
            CreateSampleQuiz(1, "My Quiz 1", creatorId: creatorId),
            CreateSampleQuiz(2, "My Quiz 2", creatorId: creatorId)
        };
        _mockRepo.Setup(r => r.FindByCreatorIdAsync(creatorId)).ReturnsAsync(quizzes);

        // Act
        var result = await _service.GetByCreatorIdAsync(creatorId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task GetByCreatorIdAsync_NoQuizzes_ReturnsEmptyList()
    {
        // Arrange
        var creatorId = 999L;
        _mockRepo.Setup(r => r.FindByCreatorIdAsync(creatorId)).ReturnsAsync(new List<Quiz>());

        // Act
        var result = await _service.GetByCreatorIdAsync(creatorId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // ========== GetPublicQuizzesAsync Tests ==========

    [Fact]
    public async Task GetPublicQuizzesAsync_WithResults_ReturnsQuizzesAndCount()
    {
        // Arrange
        var quizzes = new List<Quiz>
        {
            CreateSampleQuiz(1, "Public Quiz 1"),
            CreateSampleQuiz(2, "Public Quiz 2")
        };
        _mockCache.Setup(c => c.GetAsync<(List<PublicQuizResponse>?, int)?>(It.IsAny<string>()))
            .ReturnsAsync(((List<PublicQuizResponse>?, int)?)null);
        _mockRepo.Setup(r => r.FindPublicQuizzesAsync(null, 1, 10)).ReturnsAsync(quizzes);
        _mockRepo.Setup(r => r.GetPublicQuizzesCountAsync(null)).ReturnsAsync(2);

        // Act
        var result = await _service.GetPublicQuizzesAsync(null, 1, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
    }



    [Fact]
    public async Task GetPublicQuizzesAsync_EmptyResults_ReturnsZeroCount()
    {
        // Arrange
        _mockCache.Setup(c => c.GetAsync<(List<PublicQuizResponse>?, int)?>(It.IsAny<string>()))
            .ReturnsAsync(((List<PublicQuizResponse>?, int)?)null);
        _mockRepo.Setup(r => r.FindPublicQuizzesAsync(null, 1, 10)).ReturnsAsync(new List<Quiz>());
        _mockRepo.Setup(r => r.GetPublicQuizzesCountAsync(null)).ReturnsAsync(0);

        // Act
        var result = await _service.GetPublicQuizzesAsync(null, 1, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task GetPublicQuizzesAsync_WithSearch_PassesSearchToRepository()
    {
        // Arrange
        var search = "test";
        _mockCache.Setup(c => c.GetAsync<(List<PublicQuizResponse>?, int)?>(It.IsAny<string>()))
            .ReturnsAsync(((List<PublicQuizResponse>?, int)?)null);
        _mockRepo.Setup(r => r.FindPublicQuizzesAsync(search, 1, 10)).ReturnsAsync(new List<Quiz>());
        _mockRepo.Setup(r => r.GetPublicQuizzesCountAsync(search)).ReturnsAsync(0);

        // Act
        await _service.GetPublicQuizzesAsync(search, 1, 10);

        // Assert
        _mockRepo.Verify(r => r.FindPublicQuizzesAsync(search, 1, 10), Times.Once);
    }

    // ========== CreateAsync Tests ==========

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsCreatedQuiz()
    {
        // Arrange
        var request = CreateValidQuizRequest();
        var savedQuiz = CreateSampleQuiz(1L, "Created Quiz", "CODE123");
        _mockRepo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).ReturnsAsync(savedQuiz);
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(1L)).ReturnsAsync(savedQuiz);
        _mockRepo.Setup(r => r.FindByGameCodeAsync(It.IsAny<string>())).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.CreateAsync(request, creatorId: 1L);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Created Quiz", result.Value.Nombre);
    }

    [Fact]
    public async Task CreateAsync_EmptyQuestions_ReturnsValidationError()
    {
        // Arrange
        var request = new CreateQuizRequest
        {
            Nombre = "Test Quiz",
            EsPublico = false,
            Preguntas = new List<CreatePreguntaRequest>()
        };

        // Act
        var result = await _service.CreateAsync(request, creatorId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_QuestionWithoutCorrectAnswer_ReturnsValidationError()
    {
        // Arrange
        var request = new CreateQuizRequest
        {
            Nombre = "Test Quiz",
            EsPublico = false,
            Preguntas = new List<CreatePreguntaRequest>
            {
                new()
                {
                    NumeroPregunta = 1,
                    Enunciado = "Pregunta 1",
                    Respuestas = new List<CreateRespuestaRequest>
                    {
                        new() { Texto = "Respuesta A", EsCorrecta = false },
                        new() { Texto = "Respuesta B", EsCorrecta = false }
                    }
                }
            }
        };

        // Act
        var result = await _service.CreateAsync(request, creatorId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_QuestionWithMultipleCorrectAnswers_ReturnsValidationError()
    {
        // Arrange
        var request = new CreateQuizRequest
        {
            Nombre = "Test Quiz",
            EsPublico = false,
            Preguntas = new List<CreatePreguntaRequest>
            {
                new()
                {
                    NumeroPregunta = 1,
                    Enunciado = "Pregunta 1",
                    Respuestas = new List<CreateRespuestaRequest>
                    {
                        new() { Texto = "Respuesta A", EsCorrecta = true },
                        new() { Texto = "Respuesta B", EsCorrecta = true },
                        new() { Texto = "Respuesta C", EsCorrecta = false }
                    }
                }
            }
        };

        // Act
        var result = await _service.CreateAsync(request, creatorId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_QuestionWithLessThanTwoAnswers_ReturnsValidationError()
    {
        // Arrange
        var request = new CreateQuizRequest
        {
            Nombre = "Test Quiz",
            EsPublico = false,
            Preguntas = new List<CreatePreguntaRequest>
            {
                new()
                {
                    NumeroPregunta = 1,
                    Enunciado = "Pregunta 1",
                    Respuestas = new List<CreateRespuestaRequest>
                    {
                        new() { Texto = "Solo una respuesta", EsCorrecta = true }
                    }
                }
            }
        };

        // Act
        var result = await _service.CreateAsync(request, creatorId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task CreateAsync_QuestionWithNullAnswers_ReturnsValidationError()
    {
        // Arrange
        var request = new CreateQuizRequest
        {
            Nombre = "Test Quiz",
            EsPublico = false,
            Preguntas = new List<CreatePreguntaRequest>
            {
                new()
                {
                    NumeroPregunta = 1,
                    Enunciado = "Pregunta 1",
                    Respuestas = null!
                }
            }
        };

        // Act
        var result = await _service.CreateAsync(request, creatorId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }



    [Fact]
    public async Task CreateAsync_SaveThrows_ReturnsFailure()
    {
        // Arrange
        var request = CreateValidQuizRequest();
        _mockRepo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).ThrowsAsync(new Exception("DB error"));

        // Act
        var result = await _service.CreateAsync(request, creatorId: 1L);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateAsync_FindByIdWithQuestionsAfterSave_ReturnsNull_ReturnsNotFoundError()
    {
        // Arrange
        var request = CreateValidQuizRequest();
        var savedQuiz = CreateSampleQuiz(1L, "Created Quiz", "CODE123");
        _mockRepo.Setup(r => r.SaveAsync(It.IsAny<Quiz>())).ReturnsAsync(savedQuiz);
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(1L)).ReturnsAsync((Quiz?)null);
        _mockRepo.Setup(r => r.FindByGameCodeAsync(It.IsAny<string>())).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.CreateAsync(request, creatorId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    // ========== UpdateAsync Tests ==========

    [Fact]
    public async Task UpdateAsync_NonExistingId_ReturnsNotFoundError()
    {
        // Arrange
        var request = CreateValidUpdateQuizRequest();
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(999L)).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.UpdateAsync(999L, request, userId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    [Fact]
    public async Task UpdateAsync_NonOwnerTriesToUpdate_ReturnsForbiddenError()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Test Quiz");
        quiz.CreatorId = 1L;
        var request = CreateValidUpdateQuizRequest();
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(quizId)).ReturnsAsync(quiz);

        // Act - User 2 tries to update
        var result = await _service.UpdateAsync(quizId, request, userId: 2L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizForbiddenError>(result.Error);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_ReturnsUpdatedQuiz()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Original Quiz");
        quiz.CreatorId = 1L;
        var request = CreateValidUpdateQuizRequest();
        request = request with { Nombre = "Updated Quiz" };

        var updatedQuiz = CreateSampleQuiz(quizId, "Updated Quiz");
        updatedQuiz.CreatorId = 1L;

        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(quizId)).ReturnsAsync(quiz);
        _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<Quiz>())).Returns(Task.FromResult<Quiz>(null!));
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(quizId)).ReturnsAsync(updatedQuiz);

        // Act
        var result = await _service.UpdateAsync(quizId, request, userId: 1L);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Quiz", result.Value.Nombre);
    }

    [Fact]
    public async Task UpdateAsync_ValidationError_ReturnsValidationError()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Test Quiz");
        quiz.CreatorId = 1L;
        var request = new UpdateQuizRequest
        {
            Nombre = "Updated",
            Preguntas = new List<UpdatePreguntaRequest>() // Empty questions
        };

        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(quizId)).ReturnsAsync(quiz);

        // Act
        var result = await _service.UpdateAsync(quizId, request, userId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizValidationError>(result.Error);
    }

    [Fact]
    public async Task UpdateAsync_FindByIdWithQuestionsAfterUpdate_ReturnsNull_ReturnsNotFoundError()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Original Quiz");
        quiz.CreatorId = 1L;
        var request = CreateValidUpdateQuizRequest();

        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(quizId))
            .ReturnsAsync(quiz); // First call returns quiz
        _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<Quiz>())).Returns(Task.FromResult<Quiz>(null!));
        // Second call (after update) returns null
        _mockRepo.Setup(r => r.FindByIdWithQuestionsAsync(quizId))
            .ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.UpdateAsync(quizId, request, userId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }



    // ========== DeleteAsync Tests ==========

    [Fact]
    public async Task DeleteAsync_NonExistingId_ReturnsNotFoundError()
    {
        // Arrange
        var quizId = 999L;
        _mockRepo.Setup(r => r.FindByIdAsync(quizId)).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.DeleteAsync(quizId, userId: 1L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    [Fact]
    public async Task DeleteAsync_NonOwnerTriesToDelete_ReturnsForbiddenError()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Test Quiz");
        quiz.CreatorId = 1L; // Owner is user 1
        _mockRepo.Setup(r => r.FindByIdAsync(quizId)).ReturnsAsync(quiz);

        // Act - User 2 tries to delete
        var result = await _service.DeleteAsync(quizId, userId: 2L);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizForbiddenError>(result.Error);
    }

    [Fact]
    public async Task DeleteAsync_OwnerDeletes_ReturnsSuccess()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Test Quiz");
        quiz.CreatorId = 1L;
        _mockRepo.Setup(r => r.FindByIdAsync(quizId)).ReturnsAsync(quiz);
        _mockRepo.Setup(r => r.DeleteAsync(quizId)).Returns(Task.FromResult<Quiz>(null!));

        // Act
        var result = await _service.DeleteAsync(quizId, userId: 1L);

        // Assert
        Assert.True(result.IsSuccess);
        _mockRepo.Verify(r => r.DeleteAsync(quizId), Times.Once);
    }



    // ========== Increment/Decrement Likes/Visitas Tests ==========

    [Fact]
    public async Task IncrementLikesAsync_ExistingQuiz_ReturnsNewLikeCount()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Test Quiz");
        var updatedQuiz = CreateSampleQuiz(quizId, "Test Quiz");
        updatedQuiz.Likes = 5;
        _mockRepo.Setup(r => r.FindByIdAsync(quizId)).ReturnsAsync(quiz);
        _mockRepo.Setup(r => r.IncrementLikesAsync(quizId)).ReturnsAsync(updatedQuiz);

        // Act
        var result = await _service.IncrementLikesAsync(quizId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public async Task IncrementLikesAsync_NonExistingQuiz_ReturnsNotFoundError()
    {
        // Arrange
        var quizId = 999L;
        _mockRepo.Setup(r => r.FindByIdAsync(quizId)).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.IncrementLikesAsync(quizId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    [Fact]
    public async Task DecrementLikesAsync_NonExistingQuiz_ReturnsNotFoundError()
    {
        // Arrange
        var quizId = 999L;
        _mockRepo.Setup(r => r.FindByIdAsync(quizId)).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.DecrementLikesAsync(quizId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    [Fact]
    public async Task DecrementLikesAsync_ExistingQuiz_ReturnsNewLikeCount()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Test Quiz");
        quiz.Likes = 5;
        var updatedQuiz = CreateSampleQuiz(quizId, "Test Quiz");
        updatedQuiz.Likes = 4;
        _mockRepo.Setup(r => r.FindByIdAsync(quizId)).ReturnsAsync(quiz);
        _mockRepo.Setup(r => r.DecrementLikesAsync(quizId)).ReturnsAsync(updatedQuiz);

        // Act
        var result = await _service.DecrementLikesAsync(quizId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value);
    }

    [Fact]
    public async Task IncrementVisitasAsync_ExistingQuiz_ReturnsNewVisitasCount()
    {
        // Arrange
        var quizId = 1L;
        var quiz = CreateSampleQuiz(quizId, "Test Quiz");
        var updatedQuiz = CreateSampleQuiz(quizId, "Test Quiz");
        updatedQuiz.Visitas = 10;
        _mockRepo.Setup(r => r.FindByIdAsync(quizId)).ReturnsAsync(quiz);
        _mockRepo.Setup(r => r.IncrementVisitasAsync(quizId)).ReturnsAsync(updatedQuiz);

        // Act
        var result = await _service.IncrementVisitasAsync(quizId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public async Task IncrementVisitasAsync_NonExistingQuiz_ReturnsNotFoundError()
    {
        // Arrange
        var quizId = 999L;
        _mockRepo.Setup(r => r.FindByIdAsync(quizId)).ReturnsAsync((Quiz?)null);

        // Act
        var result = await _service.IncrementVisitasAsync(quizId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<QuizNotFoundError>(result.Error);
    }

    // ========== Helper Methods ==========

    private static Quiz CreateSampleQuiz(long id, string nombre, string gameCode = "TEST12", long creatorId = 1L)
    {
        return new Quiz
        {
            Id = id,
            Nombre = nombre,
            GameCode = gameCode,
            CreatorId = creatorId,
            EsPublico = true,
            Likes = 0,
            Visitas = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Preguntas = new List<Pregunta>
            {
                new()
                {
                    Id = id * 100,
                    QuizId = id,
                    NumeroPregunta = 1,
                    Enunciado = "Sample question",
                    CreatorId = creatorId,
                    Respuestas = new List<Respuesta>
                    {
                        new() { Id = id * 100 + 1, Texto = "Answer A", EsCorrecta = true },
                        new() { Id = id * 100 + 2, Texto = "Answer B", EsCorrecta = false }
                    }
                }
            }
        };
    }

    private static CreateQuizRequest CreateValidQuizRequest()
    {
        return new CreateQuizRequest
        {
            Nombre = "Test Quiz",
            EsPublico = true,
            Preguntas = new List<CreatePreguntaRequest>
            {
                new()
                {
                    NumeroPregunta = 1,
                    Enunciado = "What is 2+2?",
                    Respuestas = new List<CreateRespuestaRequest>
                    {
                        new() { Texto = "3", EsCorrecta = false },
                        new() { Texto = "4", EsCorrecta = true }
                    }
                }
            }
        };
    }

    private static UpdateQuizRequest CreateValidUpdateQuizRequest()
    {
        return new UpdateQuizRequest
        {
            Nombre = "Updated Quiz",
            Preguntas = new List<UpdatePreguntaRequest>
            {
                new()
                {
                    NumeroPregunta = 1,
                    Enunciado = "What is 3+3?",
                    Respuestas = new List<UpdateRespuestaRequest>
                    {
                        new() { Texto = "5", EsCorrecta = false },
                        new() { Texto = "6", EsCorrecta = true }
                    }
                }
            }
        };
    }
}
