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

    /// <summary>Dificultad: facil, media o dificil (opcional).</summary>
    [MaxLength(10, ErrorMessage = "Dificultad no válida")]
    public string? Dificultad { get; init; }

    /// <summary>Categoría existente del usuario (tiene prioridad sobre <see cref="CategoriaNombre"/>).</summary>
    public long? CategoriaId { get; init; }

    /// <summary>Nombre de categoría: se usa la existente con ese nombre o se crea una nueva.</summary>
    [MaxLength(BancoCategoria.NombreMaxLength, ErrorMessage = "El nombre de la categoría es demasiado largo")]
    public string? CategoriaNombre { get; init; }
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

    [property: JsonPropertyName("dificultad")]
    public string? Dificultad { get; init; }

    [property: JsonPropertyName("categoriaId")]
    public long? CategoriaId { get; init; }

    [property: JsonPropertyName("categoriaNombre")]
    public string? CategoriaNombre { get; init; }

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
        Dificultad = Dificultades.NormalizarOSinClasificar(p.Dificultad),
        CategoriaId = p.CategoriaId,
        CategoriaNombre = p.Categoria?.Nombre,
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

/// <summary>Mover varias preguntas a una categoría (o quitarles la categoría si es null).</summary>
public record AsignarCategoriaRequest
{
    [Required(ErrorMessage = "Indica las preguntas")]
    public List<long> PreguntaIds { get; init; } = new();

    public long? CategoriaId { get; init; }
}

/// <summary>Resultado de <see cref="AsignarCategoriaRequest"/>.</summary>
public record AsignarCategoriaResponse(
    [property: JsonPropertyName("actualizadas")] int Actualizadas
);

/// <summary>Solicitud para crear o renombrar una categoría.</summary>
public record BancoCategoriaRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    [MaxLength(BancoCategoria.NombreMaxLength, ErrorMessage = "El nombre no puede exceder 50 caracteres")]
    public string Nombre { get; init; } = string.Empty;
}

/// <summary>Categoría con el número de preguntas que contiene.</summary>
public record BancoCategoriaResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("total")] int Total
);

/// <summary>Categorías del usuario y cuántas preguntas no tienen ninguna.</summary>
public record BancoCategoriasResponse(
    [property: JsonPropertyName("categorias")] List<BancoCategoriaResponse> Categorias,
    [property: JsonPropertyName("sinCategoria")] int SinCategoria
);
