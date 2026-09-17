using System.Text.Json.Serialization;
using TriviUpBackend.Cuestionarios.Entities;

namespace TriviUpBackend.Cuestionarios.DTOs;

/// <summary>
/// Información pública de un quiz para listados.
/// </summary>
public record PublicQuizResponse
{
    [property: JsonPropertyName("id")]
    public long Id { get; init; }

    [property: JsonPropertyName("titulo")]
    public string Titulo { get; init; } = string.Empty;

    [property: JsonPropertyName("descripcion")]
    public string Descripcion { get; init; } = string.Empty;

    [property: JsonPropertyName("creadorUsername")]
    public string CreadorUsername { get; init; } = string.Empty;

    [property: JsonPropertyName("visitas")]
    public int Visitas { get; init; }

    [property: JsonPropertyName("likes")]
    public int Likes { get; init; }

    [property: JsonPropertyName("preguntasCount")]
    public int PreguntasCount { get; init; }

    [property: JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; init; }

    /// <summary>
    /// Crea un PublicQuizResponse desde una entidad Quiz.
    /// </summary>
    public static PublicQuizResponse FromEntity(Quiz quiz) => new()
    {
        Id = quiz.Id,
        Titulo = quiz.Nombre,
        Descripcion = string.Empty,
        CreadorUsername = quiz.Creator?.Username ?? string.Empty,
        Visitas = quiz.Visitas,
        Likes = quiz.Likes,
        PreguntasCount = quiz.Preguntas.Count,
        FechaCreacion = quiz.CreatedAt
    };
}

/// <summary>
/// Respuesta paginada de quizzes públicos.
/// </summary>
public record PublicQuizzesResponse
{
    [property: JsonPropertyName("quizzes")]
    public List<PublicQuizResponse> Quizzes { get; init; } = new();

    [property: JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    [property: JsonPropertyName("page")]
    public int Page { get; init; }

    [property: JsonPropertyName("pageSize")]
    public int PageSize { get; init; }
}

/// <summary>
/// Respuesta genérica para contadores.
/// </summary>
public record CountResponse
{
    [property: JsonPropertyName("count")]
    public int Count { get; init; }
}