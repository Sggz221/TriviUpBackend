using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TriviUpBackend.Data;

namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Entidad de quiz o cuestionario.
/// Representa un conjunto de preguntas de trivia.
/// </summary>
[Table("quizzes")]
public class Quiz : ITimestamped
{
    [Key]
    public long Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string GameCode { get; set; } = string.Empty;

    public List<Pregunta> Preguntas { get; set; } = new();

    [Required]
    public long CreatorId { get; set; }

    [ForeignKey(nameof(CreatorId))]
    public Models.Auth.User? Creator { get; set; }

    public bool EsPublico { get; set; } = false;

    public int Visitas { get; set; } = 0;

    public int Likes { get; set; } = 0;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
