using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TriviUpBackend.Cuestionarios.Entities;

namespace TriviUpBackend.Cuestionarios.DTOs;

/// <summary>
/// Solicitud para crear o actualizar una pregunta del banco.
/// </summary>
public record BancoPreguntaRequest
{
    [Required(ErrorMessage = "El enunciado es obligatorio")]
    [MaxLength(1000, ErrorMessage = "El enunciado no puede exceder 1000 caracteres")]
    public string Enunciado { get; init; } = string.Empty;

    [MaxLength(2000, ErrorMessage = "La URL de imagen no puede exceder 2000 caracteres")]
    public string? ImagenUrl { get; init; }

    [Required(ErrorMessage = "Las respuestas son obligatorias")]
    public List<BancoRespuestaRequest> Respuestas { get; init; } = new();

    /// <summary>Etiquetas libres (se normalizan: minúsculas, sin duplicados, máx. 10 de 30 caracteres).</summary>
    public List<string> Etiquetas { get; init; } = new();
}

/// <summary>Respuesta de una pregunta del banco (entrada).</summary>
public record BancoRespuestaRequest
{
    [Required(AllowEmptyStrings = true, ErrorMessage = "El texto es obligatorio")]
    [MaxLength(500, ErrorMessage = "El texto no puede exceder 500 caracteres")]
    public string Texto { get; init; } = string.Empty;

    public bool EsCorrecta { get; init; }
}

/// <summary>Pregunta del banco (salida).</summary>
public record BancoPreguntaResponse
{
    [property: JsonPropertyName("id")]
    public long Id { get; init; }

    [property: JsonPropertyName("enunciado")]
    public string Enunciado { get; init; } = string.Empty;

    [property: JsonPropertyName("imagenUrl")]
    public string? ImagenUrl { get; init; }

    [property: JsonPropertyName("respuestas")]
    public List<BancoRespuestaResponse> Respuestas { get; init; } = new();

    [property: JsonPropertyName("etiquetas")]
    public List<string> Etiquetas { get; init; } = new();

    [property: JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; init; }

    [property: JsonPropertyName("fechaActualizacion")]
    public DateTime FechaActualizacion { get; init; }

    public static BancoPreguntaResponse FromEntity(BancoPregunta p) => new()
    {
        Id = p.Id,
        Enunciado = p.Enunciado,
        ImagenUrl = p.ImagenUrl,
        Respuestas = p.Respuestas.Select(r => new BancoRespuestaResponse { Texto = r.Texto, EsCorrecta = r.EsCorrecta }).ToList(),
        Etiquetas = p.Etiquetas,
        FechaCreacion = p.CreatedAt,
        FechaActualizacion = p.UpdatedAt
    };
}

public record BancoRespuestaResponse
{
    [property: JsonPropertyName("texto")]
    public string Texto { get; init; } = string.Empty;

    [property: JsonPropertyName("esCorrecta")]
    public bool EsCorrecta { get; init; }
}

/// <summary>Página de preguntas del banco.</summary>
public record BancoPreguntaListResponse(
    [property: JsonPropertyName("preguntas")] List<BancoPreguntaResponse> Preguntas,
    [property: JsonPropertyName("totalCount")] int TotalCount
);

/// <summary>Etiqueta del usuario con el número de preguntas que la usan.</summary>
public record EtiquetaCountResponse(
    [property: JsonPropertyName("etiqueta")] string Etiqueta,
    [property: JsonPropertyName("total")] int Total
);
