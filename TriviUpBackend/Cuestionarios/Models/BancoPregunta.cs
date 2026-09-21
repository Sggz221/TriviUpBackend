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

    /// <summary>
    /// Etiquetas en formato "|tag1|tag2|" para poder filtrar con un simple Contains
    /// en cualquier proveedor. Usar <see cref="Etiquetas"/> para leer/escribir.
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public string EtiquetasTexto { get; set; } = string.Empty;

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

    [NotMapped]
    public List<string> Etiquetas
    {
        get => EtiquetasTexto.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        set => EtiquetasTexto = value.Count == 0 ? string.Empty : $"|{string.Join('|', value)}|";
    }

    /// <summary>Texto con el que aparece una etiqueta exacta dentro de <see cref="EtiquetasTexto"/>.</summary>
    public static string TagToken(string etiqueta) => $"|{etiqueta}|";
}

/// <summary>Respuesta de una pregunta del banco.</summary>
public class BancoRespuesta
{
    public string Texto { get; set; } = string.Empty;
    public bool EsCorrecta { get; set; }
}
