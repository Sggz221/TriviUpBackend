using System.Text.Json.Serialization;
using TriviUpBackend.Cuestionarios.Entities;

namespace TriviUpBackend.Cuestionarios.DTOs;

/// <summary>
/// Respuesta completa de un quiz con todas sus preguntas y respuestas.
/// </summary>
public record QuizResponse
{
    [property: JsonPropertyName("id")]
    public long Id { get; init; }

    [property: JsonPropertyName("nombre")]
    public string Nombre { get; init; } = string.Empty;

    [property: JsonPropertyName("gameCode")]
    public string GameCode { get; init; } = string.Empty;

    [property: JsonPropertyName("preguntas")]
    public List<PreguntaResponse> Preguntas { get; init; } = new();

    [property: JsonPropertyName("creatorId")]
    public long CreatorId { get; init; }

    [property: JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; init; }

    [property: JsonPropertyName("fechaActualizacion")]
    public DateTime FechaActualizacion { get; init; }

    /// <summary>
    /// Crea un QuizResponse desde una entidad Quiz.
    /// </summary>
    public static QuizResponse FromEntity(Quiz quiz) => new()
    {
        Id = quiz.Id,
        Nombre = quiz.Nombre,
        GameCode = quiz.GameCode,
        Preguntas = quiz.Preguntas.Select(PreguntaResponse.FromEntity).ToList(),
        CreatorId = quiz.CreatorId,
        FechaCreacion = quiz.CreatedAt,
        FechaActualizacion = quiz.UpdatedAt
    };
}

/// <summary>
/// Respuesta de una pregunta dentro de un quiz.
/// </summary>
public record PreguntaResponse
{
    [property: JsonPropertyName("id")]
    public long Id { get; init; }

    [property: JsonPropertyName("numeroPregunta")]
    public int NumeroPregunta { get; init; }

    [property: JsonPropertyName("enunciado")]
    public string Enunciado { get; init; } = string.Empty;

    [property: JsonPropertyName("imagenUrl")]
    public string? ImagenUrl { get; init; }

    [property: JsonPropertyName("respuestas")]
    public List<RespuestaResponse> Respuestas { get; init; } = new();

    /// <summary>
    /// Crea una PreguntaResponse desde una entidad Pregunta.
    /// </summary>
    public static PreguntaResponse FromEntity(Pregunta pregunta) => new()
    {
        Id = pregunta.Id,
        NumeroPregunta = pregunta.NumeroPregunta,
        Enunciado = pregunta.Enunciado,
        ImagenUrl = pregunta.ImagenUrl,
        Respuestas = pregunta.Respuestas.Select(RespuestaResponse.FromEntity).ToList()
    };
}

/// <summary>
/// Respuesta de una pregunta con su texto y si es correcta.
/// </summary>
public record RespuestaResponse
{
    [property: JsonPropertyName("id")]
    public long Id { get; init; }

    [property: JsonPropertyName("texto")]
    public string Texto { get; init; } = string.Empty;

    [property: JsonPropertyName("esCorrecta")]
    public bool EsCorrecta { get; init; }

    /// <summary>
    /// Crea una RespuestaResponse desde una entidad Respuesta.
    /// </summary>
    public static RespuestaResponse FromEntity(Respuesta respuesta) => new()
    {
        Id = respuesta.Id,
        Texto = respuesta.Texto,
        EsCorrecta = respuesta.EsCorrecta
    };
}
