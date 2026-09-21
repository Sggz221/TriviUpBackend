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

    /// <summary>Dificultad: facil, media, dificil o null (ver <see cref="Dificultades"/>).</summary>
    [MaxLength(10)]
    public string? Dificultad { get; set; }

    /// <summary>Fase (bloque) a la que pertenece la pregunta, empezando en 1.</summary>
    [Required]
    public int FaseNumero { get; set; } = 1;

    /// <summary>Nombre libre de la fase (ronda, categoría, dificultad...). Igual para todas las preguntas de la fase.</summary>
    [MaxLength(100)]
    public string? FaseNombre { get; set; }

    /// <summary>Color de la fase en #rrggbb (igual para todas las preguntas de la fase); null = por defecto.</summary>
    [MaxLength(7)]
    public string? FaseColor { get; set; }

    public List<Respuesta> Respuestas { get; set; } = new();

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    public Respuesta? ObtenerRespuestaCorrecta() => Respuestas.FirstOrDefault(r => r.EsCorrecta);
}
