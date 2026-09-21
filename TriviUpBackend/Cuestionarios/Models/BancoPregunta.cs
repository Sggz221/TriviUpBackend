using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using TriviUpBackend.Data;

namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Pregunta guardada en el banco personal de un usuario, independiente de cualquier cuestionario.
/// Al añadirla a un cuestionario se copia (no queda enlazada).
/// </summary>
[Table("banco_preguntas")]
public class BancoPregunta : ITimestamped
{
    [Key]
    public long Id { get; set; }

    [Required]
    public long CreatorId { get; set; }

    [ForeignKey(nameof(CreatorId))]
    public Models.Auth.User? Creator { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Enunciado { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? ImagenUrl { get; set; }

    /// <summary>Respuestas serializadas como JSON (<see cref="BancoRespuesta"/>).</summary>
    [Required]
    public string RespuestasJson { get; set; } = "[]";

    /// <summary>Categoría del banco a la que pertenece (null = sin categoría).</summary>
    public long? CategoriaId { get; set; }

    [ForeignKey(nameof(CategoriaId))]
    public BancoCategoria? Categoria { get; set; }

    /// <summary>Dificultad: facil, media, dificil o null (ver <see cref="Dificultades"/>).</summary>
    [MaxLength(10)]
    public string? Dificultad { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    [NotMapped]
    public List<BancoRespuesta> Respuestas
    {
        get => string.IsNullOrEmpty(RespuestasJson)
            ? new List<BancoRespuesta>()
            : JsonSerializer.Deserialize<List<BancoRespuesta>>(RespuestasJson) ?? new List<BancoRespuesta>();
        set => RespuestasJson = JsonSerializer.Serialize(value);
    }
}

/// <summary>Respuesta de una pregunta del banco.</summary>
public class BancoRespuesta
{
    public string Texto { get; set; } = string.Empty;
    public bool EsCorrecta { get; set; }
}
