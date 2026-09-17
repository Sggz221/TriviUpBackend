using System.Text.Json.Serialization;

namespace TriviUpBackend.Cuestionarios.DTOs;

/// <summary>
/// Información resumida de un quiz para listados.
/// </summary>
public record QuizListResponse
{
    [property: JsonPropertyName("id")]
    public long Id { get; init; }

    [property: JsonPropertyName("nombre")]
    public string Nombre { get; init; } = string.Empty;

    [property: JsonPropertyName("gameCode")]
    public string GameCode { get; init; } = string.Empty;

    [property: JsonPropertyName("numeroPreguntas")]
    public int NumeroPreguntas { get; init; }

    [property: JsonPropertyName("creatorId")]
    public long CreatorId { get; init; }

    [property: JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; init; }

    [property: JsonPropertyName("fechaActualizacion")]
    public DateTime FechaActualizacion { get; init; }
}
