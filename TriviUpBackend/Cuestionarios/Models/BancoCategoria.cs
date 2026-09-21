using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TriviUpBackend.Data;

namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Categoría del banco personal de un usuario. Puede existir vacía; cada pregunta del banco
/// pertenece como mucho a una. El nombre es único por usuario sin distinguir mayúsculas.
/// </summary>
[Table("banco_categorias")]
public class BancoCategoria : ITimestamped
{
    public const int NombreMaxLength = 50;

    [Key]
    public long Id { get; set; }

    [Required]
    public long CreatorId { get; set; }

    [ForeignKey(nameof(CreatorId))]
    public Models.Auth.User? Creator { get; set; }

    [Required]
    [MaxLength(NombreMaxLength)]
    public string Nombre { get; set; } = string.Empty;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
