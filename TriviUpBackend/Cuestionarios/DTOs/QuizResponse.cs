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

    [property: JsonPropertyName("esPublico")]
    public bool EsPublico { get; init; }

    [property: JsonPropertyName("esBorrador")]
    public bool EsBorrador { get; init; }

    [property: JsonPropertyName("version")]
    public int Version { get; init; }

    [property: JsonPropertyName("tieneBorrador")]
    public bool TieneBorrador { get; init; }

    [property: JsonPropertyName("preguntas")]
    public List<PreguntaResponse> Preguntas { get; init; } = new();

    [property: JsonPropertyName("creatorId")]
    public long CreatorId { get; init; }

    [property: JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; init; }

    [property: JsonPropertyName("fechaActualizacion")]
    public DateTime FechaActualizacion { get; init; }

    /// <summary>
    /// Crea un QuizResponse a partir del contenido de un borrador de un quiz publicado.
    /// </summary>
    public static QuizResponse FromDraft(Quiz quiz, UpdateQuizRequest contenido, DateTime fecha) => new()
    {
        Id = quiz.Id,
        Nombre = contenido.Nombre,
        GameCode = quiz.GameCode,
        EsPublico = contenido.EsPublico ?? quiz.EsPublico,
        EsBorrador = true,
        Version = quiz.VersionPublicada,
        TieneBorrador = true,
        Preguntas = contenido.Preguntas
            .OrderBy(p => p.NumeroPregunta)
            .Select(p => new PreguntaResponse
            {
                NumeroPregunta = p.NumeroPregunta,
                Enunciado = p.Enunciado,
                ImagenUrl = p.ImagenUrl,
                FaseNumero = p.FaseNumero,
                FaseNombre = p.FaseNombre,
                FaseColor = FaseColores.NormalizarOSinColor(p.FaseColor),
                Dificultad = Dificultades.NormalizarOSinClasificar(p.Dificultad),
                Respuestas = p.Respuestas
                    .Select(r => new RespuestaResponse { Texto = r.Texto, EsCorrecta = r.EsCorrecta })
                    .ToList()
            }).ToList(),
        CreatorId = quiz.CreatorId,
        FechaCreacion = quiz.CreatedAt,
        FechaActualizacion = fecha
    };

    /// <summary>
    /// Crea un QuizResponse desde una entidad Quiz.
    /// </summary>
    public static QuizResponse FromEntity(Quiz quiz) => new()
    {
        Id = quiz.Id,
        Nombre = quiz.Nombre,
        GameCode = quiz.GameCode,
        EsPublico = quiz.EsPublico,
        EsBorrador = quiz.EsBorrador,
        Version = quiz.VersionPublicada,
        Preguntas = quiz.Preguntas.OrderBy(p => p.NumeroPregunta).Select(PreguntaResponse.FromEntity).ToList(),
        CreatorId = quiz.CreatorId,
        FechaCreacion = quiz.CreatedAt,
        FechaActualizacion = quiz.UpdatedAt
    };
}

/// <summary>
/// Entrada del historial de versiones de un quiz.
/// </summary>
public record QuizVersionResponse
{
    /// <summary>Número de versión; null en el borrador.</summary>
    [property: JsonPropertyName("numero")]
    public int? Numero { get; init; }

    /// <summary>"Publicada", "Borrador" o "Archivada".</summary>
    [property: JsonPropertyName("estado")]
    public string Estado { get; init; } = string.Empty;

    [property: JsonPropertyName("nombre")]
    public string Nombre { get; init; } = string.Empty;

    [property: JsonPropertyName("fecha")]
    public DateTime Fecha { get; init; }
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

    [property: JsonPropertyName("faseNumero")]
    public int FaseNumero { get; init; } = 1;

    [property: JsonPropertyName("faseNombre")]
    public string? FaseNombre { get; init; }

    [property: JsonPropertyName("faseColor")]
    public string? FaseColor { get; init; }

    [property: JsonPropertyName("dificultad")]
    public string? Dificultad { get; init; }

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
        FaseNumero = pregunta.FaseNumero,
        FaseNombre = pregunta.FaseNombre,
        FaseColor = pregunta.FaseColor,
        Dificultad = pregunta.Dificultad,
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
