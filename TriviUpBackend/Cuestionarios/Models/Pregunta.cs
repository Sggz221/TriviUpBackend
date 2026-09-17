using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TriviUpBackend.Data;

namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Entidad de pregunta dentro de un quiz.
/// Contiene el enunciado y sus posibles respuestas.
/// </summary>
[Table("preguntas")]
public class Pregunta : ITimestamped
{
    [Key]
    public long Id { get; set; }

    [Required]
    public long QuizId { get; set; }

    [ForeignKey(nameof(QuizId))]
    public Quiz? Quiz { get; set; }

    [Required]
    public long CreatorId { get; set; }

    [ForeignKey(nameof(CreatorId))]
    public Models.Auth.User? Creator { get; set; }

    [Required]
    public int NumeroPregunta { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Enunciado { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? ImagenUrl { get; set; }

    public List<Respuesta> Respuestas { get; set; } = new();

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    public Respuesta? ObtenerRespuestaCorrecta() => Respuestas.FirstOrDefault(r => r.EsCorrecta);
}
