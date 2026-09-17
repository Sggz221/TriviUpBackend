using TriviUpBackend.Cuestionarios.DTOs;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Models.Auth;
using Pregunta = TriviUpBackend.Cuestionarios.Entities.Pregunta;
using Respuesta = TriviUpBackend.Cuestionarios.Entities.Respuesta;
using Quiz = TriviUpBackend.Cuestionarios.Entities.Quiz;

namespace TriviUpTest.DTOs;

public class CuestionariosDtosTests
{
    // ========== QuizResponse Tests ==========

    [Fact]
    public void QuizResponse_FromEntity_MapsAllProperties()
    {
        // Arrange
        var quiz = CreateSampleQuiz();

        // Act
        var response = QuizResponse.FromEntity(quiz);

        // Assert
        Assert.Equal(quiz.Id, response.Id);
        Assert.Equal(quiz.Nombre, response.Nombre);
        Assert.Equal(quiz.GameCode, response.GameCode);
        Assert.Equal(quiz.CreatorId, response.CreatorId);
        Assert.Equal(quiz.CreatedAt, response.FechaCreacion);
        Assert.Equal(quiz.UpdatedAt, response.FechaActualizacion);
        Assert.NotEmpty(response.Preguntas);
        Assert.Single(response.Preguntas);
    }

    [Fact]
    public void QuizResponse_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var response = new QuizResponse
        {
            Id = 1,
            Nombre = "Test Quiz",
            GameCode = "TEST01",
            CreatorId = 10,
            Preguntas = new List<PreguntaResponse>(),
            FechaCreacion = DateTime.UtcNow,
            FechaActualizacion = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(1, response.Id);
        Assert.Equal("Test Quiz", response.Nombre);
        Assert.Equal("TEST01", response.GameCode);
        Assert.Equal(10, response.CreatorId);
    }

    // ========== PreguntaResponse Tests ==========

    [Fact]
    public void PreguntaResponse_FromEntity_MapsAllProperties()
    {
        // Arrange
        var pregunta = CreateSamplePregunta();

        // Act
        var response = PreguntaResponse.FromEntity(pregunta);

        // Assert
        Assert.Equal(pregunta.Id, response.Id);
        Assert.Equal(pregunta.NumeroPregunta, response.NumeroPregunta);
        Assert.Equal(pregunta.Enunciado, response.Enunciado);
        Assert.Equal(pregunta.ImagenUrl, response.ImagenUrl);
        Assert.NotEmpty(response.Respuestas);
        Assert.Equal(2, response.Respuestas.Count);
    }

    [Fact]
    public void PreguntaResponse_WithNullImagenUrl_MapsCorrectly()
    {
        // Arrange
        var pregunta = new Pregunta
        {
            Id = 1,
            QuizId = 1,
            NumeroPregunta = 1,
            Enunciado = "Question?",
            ImagenUrl = null,
            CreatorId = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Respuestas = new List<Respuesta>()
        };

        // Act
        var response = PreguntaResponse.FromEntity(pregunta);

        // Assert
        Assert.Equal(1, response.Id);
        Assert.Null(response.ImagenUrl);
        Assert.Empty(response.Respuestas);
    }

    [Fact]
    public void PreguntaResponse_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var response = new PreguntaResponse
        {
            Id = 1,
            NumeroPregunta = 1,
            Enunciado = "Test question",
            ImagenUrl = "https://example.com/image.png",
            Respuestas = new List<RespuestaResponse>()
        };

        // Assert
        Assert.Equal(1, response.Id);
        Assert.Equal("Test question", response.Enunciado);
        Assert.Equal("https://example.com/image.png", response.ImagenUrl);
    }

    // ========== RespuestaResponse Tests ==========

    [Fact]
    public void RespuestaResponse_FromEntity_MapsAllProperties()
    {
        // Arrange
        var respuesta = new Respuesta
        {
            Id = 1,
            PreguntaId = 1,
            Texto = "Answer text",
            EsCorrecta = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var response = RespuestaResponse.FromEntity(respuesta);

        // Assert
        Assert.Equal(respuesta.Id, response.Id);
        Assert.Equal(respuesta.Texto, response.Texto);
        Assert.Equal(respuesta.EsCorrecta, response.EsCorrecta);
    }

    [Fact]
    public void RespuestaResponse_CorrectAnswer_HasCorrectFlag()
    {
        // Arrange
        var respuesta = CreateSampleRespuesta(true);

        // Act
        var response = RespuestaResponse.FromEntity(respuesta);

        // Assert
        Assert.True(response.EsCorrecta);
    }

    [Fact]
    public void RespuestaResponse_IncorrectAnswer_HasIncorrectFlag()
    {
        // Arrange
        var respuesta = CreateSampleRespuesta(false);

        // Act
        var response = RespuestaResponse.FromEntity(respuesta);

        // Assert
        Assert.False(response.EsCorrecta);
    }

    // ========== PublicQuizResponse Tests ==========

    [Fact]
    public void PublicQuizResponse_FromEntity_MapsAllProperties()
    {
        // Arrange
        var quiz = CreateSampleQuiz();
        quiz.Creator = new User
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

        // Act
        var response = PublicQuizResponse.FromEntity(quiz);

        // Assert
        Assert.Equal(quiz.Id, response.Id);
        Assert.Equal(quiz.Nombre, response.Titulo);
        Assert.Equal("testuser", response.CreadorUsername);
        Assert.Equal(quiz.Visitas, response.Visitas);
        Assert.Equal(quiz.Likes, response.Likes);
        Assert.Equal(quiz.Preguntas.Count, response.PreguntasCount);
        Assert.Equal(quiz.CreatedAt, response.FechaCreacion);
    }

    [Fact]
    public void PublicQuizResponse_FromEntity_WithNullCreator_SetsEmptyUsername()
    {
        // Arrange
        var quiz = CreateSampleQuiz();
        quiz.Creator = null!;

        // Act
        var response = PublicQuizResponse.FromEntity(quiz);

        // Assert
        Assert.Equal(string.Empty, response.CreadorUsername);
    }

    // ========== PublicQuizzesResponse Tests ==========

    [Fact]
    public void PublicQuizzesResponse_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var response = new PublicQuizzesResponse
        {
            Quizzes = new List<PublicQuizResponse>(),
            TotalCount = 10,
            Page = 1,
            PageSize = 5
        };

        // Assert
        Assert.Empty(response.Quizzes);
        Assert.Equal(10, response.TotalCount);
        Assert.Equal(1, response.Page);
        Assert.Equal(5, response.PageSize);
    }

    // ========== CountResponse Tests ==========

    [Fact]
    public void CountResponse_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var response = new CountResponse { Count = 42 };

        // Assert
        Assert.Equal(42, response.Count);
    }

    // ========== Helper Methods ==========

    private static Quiz CreateSampleQuiz()
    {
        return new Quiz
        {
            Id = 1,
            Nombre = "Test Quiz",
            GameCode = "TEST01",
            CreatorId = 1,
            EsPublico = true,
            Visitas = 100,
            Likes = 50,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Preguntas = new List<Pregunta>
            {
                CreateSamplePregunta()
            }
        };
    }

    private static Pregunta CreateSamplePregunta()
    {
        return new Pregunta
        {
            Id = 1,
            QuizId = 1,
            NumeroPregunta = 1,
            Enunciado = "Sample question",
            ImagenUrl = "https://example.com/image.png",
            CreatorId = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Respuestas = new List<Respuesta>
            {
                CreateSampleRespuesta(true),
                CreateSampleRespuesta(false)
            }
        };
    }

    private static Respuesta CreateSampleRespuesta(bool esCorrecta)
    {
        return new Respuesta
        {
            Id = esCorrecta ? 1 : 2,
            PreguntaId = 1,
            Texto = esCorrecta ? "Correct answer" : "Wrong answer",
            EsCorrecta = esCorrecta,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}