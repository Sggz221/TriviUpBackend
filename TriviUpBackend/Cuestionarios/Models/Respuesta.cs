using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TriviUpBackend.Data;

namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Entidad de respuesta a una pregunta.
/// </summary>
[Table("respuestas")]
public class Respuesta : ITimestamped
{
    [Key]
    public long Id { get; set; }

    [Required]
    public long PreguntaId { get; set; }

    [ForeignKey(nameof(PreguntaId))]
    public Pregunta? Pregunta { get; set; }

    [Required]
    [MaxLength(500)]
    public string Texto { get; set; } = string.Empty;

    [Required]
    public bool EsCorrecta { get; set; } = false;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
