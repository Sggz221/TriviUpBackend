using System.ComponentModel.DataAnnotations;

namespace TriviUpBackend.Cuestionarios.DTOs;

/// <summary>
/// Solicitud para actualizar un quiz existente.
/// </summary>
public record UpdateQuizRequest
{
    /// <summary>
    /// Nombre del quiz.
    /// </summary>
    [Required(ErrorMessage = "El nombre es obligatorio")]
    [MinLength(1, ErrorMessage = "El nombre no puede estar vacío")]
    [MaxLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    public string Nombre { get; init; } = string.Empty;

    /// <summary>
    /// Lista de preguntas del quiz (se reemplazan las existentes).
    /// </summary>
    [Required(ErrorMessage = "Las preguntas son obligatorias")]
    [MinLength(1, ErrorMessage = "Debe haber al menos una pregunta")]
    public List<UpdatePreguntaRequest> Preguntas { get; init; } = new();
}

/// <summary>
/// Solicitud para actualizar una pregunta dentro de un quiz.
/// </summary>
public record UpdatePreguntaRequest
{
    /// <summary>
    /// ID de la pregunta (null si es nueva pregunta).
    /// </summary>
    public long? Id { get; init; }

    /// <summary>
    /// Número de orden de la pregunta.
    /// </summary>
    [Required(ErrorMessage = "El número de pregunta es obligatorio")]
    [Range(1, int.MaxValue, ErrorMessage = "El número de pregunta debe ser mayor a 0")]
    public int NumeroPregunta { get; init; }

    /// <summary>
    /// Texto del enunciado de la pregunta.
    /// </summary>
    [Required(ErrorMessage = "El enunciado es obligatorio")]
    [MinLength(1, ErrorMessage = "El enunciado no puede estar vacío")]
    [MaxLength(1000, ErrorMessage = "El enunciado no puede exceder 1000 caracteres")]
    public string Enunciado { get; init; } = string.Empty;

    /// <summary>
    /// URL de la imagen asociada a la pregunta (opcional).
    /// </summary>
    [MaxLength(2000, ErrorMessage = "La URL de imagen no puede exceder 2000 caracteres")]
    public string? ImagenUrl { get; init; }

    /// <summary>
    /// Lista de respuestas de la pregunta.
    /// </summary>
    [Required(ErrorMessage = "Las respuestas son obligatorias")]
    [MinLength(2, ErrorMessage = "Debe haber al menos 2 respuestas")]
    public List<UpdateRespuestaRequest> Respuestas { get; init; } = new();
}

/// <summary>
/// Solicitud para actualizar una respuesta de una pregunta.
/// </summary>
public record UpdateRespuestaRequest
{
    /// <summary>
    /// ID de la respuesta (null si es nueva respuesta).
    /// </summary>
    public long? Id { get; init; }

    /// <summary>
    /// Texto de la respuesta.
    /// </summary>
    [Required(ErrorMessage = "El texto es obligatorio")]
    [MinLength(1, ErrorMessage = "El texto no puede estar vacío")]
    [MaxLength(500, ErrorMessage = "El texto no puede exceder 500 caracteres")]
    public string Texto { get; init; } = string.Empty;

    /// <summary>
    /// Indica si esta es la respuesta correcta.
    /// </summary>
    public bool EsCorrecta { get; init; } = false;
}
