using System.ComponentModel.DataAnnotations;

namespace TriviUpBackend.DTO.User;

/// <summary>
/// DTO para el registro de un nuevo usuario.
/// </summary>
public record RegisterDto
{
    /// <summary>
    /// Nombre de usuario único (3-50 caracteres, solo letras, números y guiones bajos).
    /// </summary>
    [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
    [MinLength(3, ErrorMessage = "El nombre de usuario debe tener al menos 3 caracteres")]
    [MaxLength(50, ErrorMessage = "El nombre de usuario no puede exceder 50 caracteres")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Solo se permiten letras, números y guiones bajos")]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Correo electrónico del usuario.
    /// </summary>
    [Required(ErrorMessage = "El correo electrónico es obligatorio")]
    [EmailAddress(ErrorMessage = "Debe ser un correo electrónico válido")]
    [MaxLength(100, ErrorMessage = "El correo no puede exceder 100 caracteres")]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Contraseña del usuario (mínimo 4 caracteres).
    /// </summary>
    [Required(ErrorMessage = "La contraseña es obligatoria")]
    [MinLength(4, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
    [MaxLength(100, ErrorMessage = "La contraseña no puede exceder 100 caracteres")]
    public string Password { get; init; } = string.Empty;
}