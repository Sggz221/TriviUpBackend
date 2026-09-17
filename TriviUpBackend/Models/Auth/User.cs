using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Data;

namespace TriviUpBackend.Models.Auth;

/// <summary>
/// Entidad de usuario en el sistema.
/// Representa un usuario registrado con credenciales de acceso.
/// </summary>
[Table("users")]
[Index(nameof(Username), IsUnique = true)]
[Index(nameof(Email), IsUnique = true)]
[Index(nameof(GoogleId), IsUnique = true)]
public class User : ITimestamped
{
    [Key]
    public long Id { get; set; }
   
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [EmailAddress] // Validación extra para el formato
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = UserRoles.USER;

    public bool IsDeleted { get; set; } = false;

    public bool IsBanned { get; set; } = false;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    [MaxLength(255)]
    public string? GoogleId { get; set; }

    /// <summary>
    /// Ruta relativa de la foto de perfil (ej: "profile-photos/123/profile.jpg")
    /// </summary>
    [MaxLength(500)]
    public string? ProfilePhotoUrl { get; set; }

    /// <summary>
    /// Última fecha de inicio de sesión del usuario.
    /// Se usa para determinar si un usuario está "activo" (últimos 7 días).
    /// </summary>
    public DateTime? LastLoginAt { get; set; }
}