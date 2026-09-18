using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Estado de una versión almacenada. La versión publicada vive en las tablas
/// quizzes/preguntas/respuestas; aquí solo se guardan borradores e historial.
/// </summary>
public enum QuizVersionEstado
{
    Borrador,
    Archivada
}

/// <summary>
/// Copia del contenido de un quiz: el borrador pendiente de un quiz ya publicado
/// (Numero null) o una versión publicada anteriormente (Archivada).
/// </summary>
[Table("quiz_versions")]
public class QuizVersion
{
    [Key]
    public long Id { get; set; }

    [Required]
    public long QuizId { get; set; }

    [ForeignKey(nameof(QuizId))]
    public Quiz? Quiz { get; set; }

    /// <summary>Número de versión publicada. Null en el borrador.</summary>
    public int? Numero { get; set; }

    public QuizVersionEstado Estado { get; set; }

    [Required]
    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    public bool EsPublico { get; set; }

    /// <summary>Contenido serializado (UpdateQuizRequest) en jsonb.</summary>
    [Required]
    public string Contenido { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PublishedAt { get; set; }
}
