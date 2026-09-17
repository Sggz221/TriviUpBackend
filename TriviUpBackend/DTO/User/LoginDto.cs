using System.ComponentModel.DataAnnotations;

namespace TriviUpBackend.DTO.User;

/// <summary>
/// DTO para el inicio de sesión de un usuario.
/// </summary>
public record LoginDto
{
    /// <summary>
    /// Nombre de usuario.
    /// </summary>
    [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Contraseña del usuario.
    /// </summary>
    [Required(ErrorMessage = "La contraseña es obligatoria")]
    public string Password { get; init; } = string.Empty;
};