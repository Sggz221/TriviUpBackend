using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Database;
using TriviUpBackend.Models.Auth;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;

namespace TriviUpTest.Repositories;

public class QuizRepositoryTests : IDisposable
{
    private readonly Context _context;
    private readonly QuizRepository _repository;
    private readonly Mock<ILogger<QuizRepository>> _mockLogger;

    public QuizRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<Context>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new Context(options);
        _mockLogger = new Mock<ILogger<QuizRepository>>();
        _repository = new QuizRepository(_context, _mockLogger.Object);

        SeedDatabase();
    }

    private void SeedDatabase()
    {
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            Email = "test@test.com",
            PasswordHash = "hash",
            Role = UserRoles.USER,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var quiz = new Quiz
        {
            Id = 1,
            Nombre = "Test Quiz",
            GameCode = "TEST01",
            CreatorId = 1,
            EsPublico = true,
            Visitas = 10,
            Likes = 5,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var pregunta = new Pregunta
        {
            Id = 1,
            QuizId = 1,
            CreatorId = 1,
            NumeroPregunta = 1,
            Enunciado = "Test question",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var respuesta1 = new Respuesta
        {
            Id = 1,
            PreguntaId = 1,
            Texto = "Answer A",
            EsCorrecta = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var respuesta2 = new Respuesta
        {
            Id = 2,
            PreguntaId = 1,
            Texto = "Answer B",
            EsCorrecta = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.Quizzes.Add(quiz);
        _context.Preguntas.Add(pregunta);
        _context.Respuestas.AddRange(respuesta1, respuesta2);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ========== FindByIdAsync Tests ==========

    [Fact]
    public async Task FindByIdAsync_ExistingId_ReturnsQuiz()
    {
        // Act
        var result = await _repository.FindByIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Test Quiz", result.Nombre);
    }

    [Fact]
    public async Task FindByIdAsync_NonExistingId_ReturnsNull()
    {
        // Act
        var result = await _repository.FindByIdAsync(999);

        // Assert
        Assert.Null(result);
    }

    // ========== FindByGameCodeAsync Tests ==========

    [Fact]
    public async Task FindByGameCodeAsync_ExistingCode_ReturnsQuiz()
    {
        // Act
        var result = await _repository.FindByGameCodeAsync("TEST01");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TEST01", result.GameCode);
    }

    [Fact]
    public async Task FindByGameCodeAsync_NonExistingCode_ReturnsNull()
    {
        // Act
        var result = await _repository.FindByGameCodeAsync("NONEXIST");

        // Assert
        Assert.Null(result);
    }

    // ========== FindByIdWithQuestionsAsync Tests ==========

    [Fact]
    public async Task FindByIdWithQuestionsAsync_ExistingId_ReturnsQuizWithQuestions()
    {
        // Act
        var result = await _repository.FindByIdWithQuestionsAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.NotNull(result.Preguntas);
        Assert.Single(result.Preguntas);
    }

    [Fact]
    public async Task FindByIdWithQuestionsAsync_NonExistingId_ReturnsNull()
    {
        // Act
        var result = await _repository.FindByIdWithQuestionsAsync(999);

        // Assert
        Assert.Null(result);
    }

    // ========== FindByQuestionIdAsync Tests ==========

    [Fact]
    public async Task FindByQuestionIdAsync_ExistingQuestionId_ReturnsQuiz()
    {
        // Act
        var result = await _repository.FindByQuestionIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }

    [Fact]
    public async Task FindByQuestionIdAsync_NonExistingQuestionId_ReturnsNull()
    {
        // Act
        var result = await _repository.FindByQuestionIdAsync(999);

        // Assert
        Assert.Null(result);
    }

    // ========== FindAllAsync Tests ==========

    [Fact]
    public async Task FindAllAsync_ReturnsPagedQuizzes()
    {
        // Act
        var result = await _repository.FindAllAsync(1, 10);

        // Assert
        var quizzes = result.ToList();
        Assert.NotEmpty(quizzes);
        Assert.Single(quizzes);
    }

    [Fact]
    public async Task FindAllAsync_SecondPage_ReturnsEmptyForSinglePageData()
    {
        // Act
        var result = await _repository.FindAllAsync(2, 10);

        // Assert
        Assert.Empty(result);
    }

    // ========== FindByCreatorIdAsync Tests ==========

    [Fact]
    public async Task FindByCreatorIdAsync_ExistingCreator_ReturnsQuizzes()
    {
        // Act
        var result = await _repository.FindByCreatorIdAsync(1);

        // Assert
        var quizzes = result.ToList();
        Assert.NotEmpty(quizzes);
        Assert.Single(quizzes);
    }

    [Fact]
    public async Task FindByCreatorIdAsync_NonExistingCreator_ReturnsEmpty()
    {
        // Act
        var result = await _repository.FindByCreatorIdAsync(999);

        // Assert
        Assert.Empty(result);
    }

    // ========== FindPublicQuizzesAsync Tests ==========

    [Fact]
    public async Task FindPublicQuizzesAsync_ReturnsPublicQuizzes()
    {
        // Act
        var result = await _repository.FindPublicQuizzesAsync(null, 1, 10);

        // Assert
        var quizzes = result.ToList();
        Assert.NotEmpty(quizzes);
    }

    [Fact]
    public async Task FindPublicQuizzesAsync_WithSearch_FiltersByName()
    {
        // Act
        var result = await _repository.FindPublicQuizzesAsync("Test", 1, 10);

        // Assert
        var quizzes = result.ToList();
        Assert.Single(quizzes);
        Assert.Contains("Test", quizzes[0].Nombre);
    }

    [Fact]
    public async Task FindPublicQuizzesAsync_NoMatch_ReturnsEmpty()
    {
        // Act
        var result = await _repository.FindPublicQuizzesAsync("NonExistentQuiz", 1, 10);

        // Assert
        Assert.Empty(result);
    }

    // ========== GetPublicQuizzesCountAsync Tests ==========

    [Fact]
    public async Task GetPublicQuizzesCountAsync_ReturnsCorrectCount()
    {
        // Act
        var result = await _repository.GetPublicQuizzesCountAsync(null);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task GetPublicQuizzesCountAsync_WithSearch_ReturnsFilteredCount()
    {
        // Act
        var result = await _repository.GetPublicQuizzesCountAsync("Test");

        // Assert
        Assert.Equal(1, result);
    }

    // ========== FindPublicByIdAsync Tests ==========

    [Fact]
    public async Task FindPublicByIdAsync_PublicQuiz_ReturnsQuiz()
    {
        // Act
        var result = await _repository.FindPublicByIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }

    [Fact]
    public async Task FindPublicByIdAsync_PrivateQuiz_ReturnsNull()
    {
        // Arrange - Add a private quiz
        var privateQuiz = new Quiz
        {
            Id = 100,
            Nombre = "Private Quiz",
            GameCode = "PRIV99",
            CreatorId = 1,
            EsPublico = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Quizzes.Add(privateQuiz);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.FindPublicByIdAsync(100);

        // Assert
        Assert.Null(result);
    }

    // ========== IncrementVisitasAsync Tests ==========

    [Fact]
    public async Task IncrementVisitasAsync_ExistingQuiz_IncrementsVisitas()
    {
        // Act
        var result = await _repository.IncrementVisitasAsync(1);

        // Assert
        Assert.Equal(11, result.Visitas);
    }

    [Fact]
    public async Task IncrementVisitasAsync_NonExistingQuiz_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.IncrementVisitasAsync(999));
    }

    // ========== IncrementLikesAsync Tests ==========

    [Fact]
    public async Task IncrementLikesAsync_ExistingQuiz_IncrementsLikes()
    {
        // Act
        var result = await _repository.IncrementLikesAsync(1);

        // Assert
        Assert.Equal(6, result.Likes);
    }

    [Fact]
    public async Task IncrementLikesAsync_NonExistingQuiz_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.IncrementLikesAsync(999));
    }

    // ========== DecrementLikesAsync Tests ==========

    [Fact]
    public async Task DecrementLikesAsync_ExistingQuizWithLikes_DecrementsLikes()
    {
        // Act
        var result = await _repository.DecrementLikesAsync(1);

        // Assert
        Assert.Equal(4, result.Likes);
    }

    [Fact]
    public async Task DecrementLikesAsync_QuizWithZeroLikes_StaysAtZero()
    {
        // Arrange - Set likes to 0
        var quiz = await _context.Quizzes.FindAsync(1L);
        quiz!.Likes = 0;
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.DecrementLikesAsync(1);

        // Assert
        Assert.Equal(0, result.Likes);
    }

    [Fact]
    public async Task DecrementLikesAsync_NonExistingQuiz_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.DecrementLikesAsync(999));
    }

    // ========== SaveAsync Tests ==========

    [Fact]
    public async Task SaveAsync_NewQuiz_SavesAndReturnsQuiz()
    {
        // Arrange
        var newQuiz = new Quiz
        {
            Nombre = "New Quiz",
            GameCode = "NEW001",
            CreatorId = 1,
            EsPublico = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _repository.SaveAsync(newQuiz);

        // Assert
        Assert.True(result.Id > 0);
        Assert.Equal("New Quiz", result.Nombre);
    }

    // ========== UpdateAsync Tests ==========

    [Fact]
    public async Task UpdateAsync_ExistingQuiz_UpdatesQuiz()
    {
        // Arrange
        var quiz = await _repository.FindByIdAsync(1);
        quiz!.Nombre = "Updated Quiz";

        // Act
        var result = await _repository.UpdateAsync(quiz);

        // Assert
        Assert.Equal("Updated Quiz", result.Nombre);
    }

    // ========== DeleteAsync Tests ==========

    [Fact]
    public async Task DeleteAsync_ExistingQuiz_DeletesQuiz()
    {
        // Arrange - Add a quiz to delete
        var quizToDelete = new Quiz
        {
            Id = 200,
            Nombre = "To Delete",
            GameCode = "DEL001",
            CreatorId = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Quizzes.Add(quizToDelete);
        await _context.SaveChangesAsync();

        // Act
        await _repository.DeleteAsync(200);

        // Assert
        var deleted = await _context.Quizzes.FindAsync(200L);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingQuiz_DoesNotThrow()
    {
        // Act & Assert - should not throw
        await _repository.DeleteAsync(999);
    }

    // ========== GetTotalCountAsync Tests ==========

    [Fact]
    public async Task GetTotalCountAsync_ReturnsCorrectCount()
    {
        // Act
        var result = await _repository.GetTotalCountAsync();

        // Assert
        Assert.True(result > 0);
    }

    // ========== GetQuestionsWithAnswersAsync Tests ==========

    [Fact]
    public async Task GetQuestionsWithAnswersAsync_ExistingQuizId_ReturnsQuestions()
    {
        // Act
        var result = await _repository.GetQuestionsWithAnswersAsync(1);

        // Assert
        var preguntas = result.ToList();
        Assert.Single(preguntas);
        Assert.NotEmpty(preguntas[0].Respuestas);
    }

    [Fact]
    public async Task GetQuestionsWithAnswersAsync_NonExistingQuizId_ReturnsEmpty()
    {
        // Act
        var result = await _repository.GetQuestionsWithAnswersAsync(999);

        // Assert
        Assert.Empty(result);
    }
}