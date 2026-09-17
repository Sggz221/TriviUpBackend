using TriviUpBackend.Models.Auth;

namespace TriviUpBackend.Services.Auth;

/// <summary>
/// Interfaz para el servicio de generación y validación de tokens JWT.
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Genera un token JWT para un usuario.
    /// </summary>
    /// <param name="user">Usuario para el cual generar el token.</param>
    /// <returns>Token JWT generado como cadena.</returns>
    string GenerateToken(User user);

    /// <summary>
    /// Valida un token JWT y devuelve el identificador del usuario.
    /// </summary>
    /// <param name="token">Token JWT a validar.</param>
    /// <returns>Nombre de usuario si es válido, null en caso contrario.</returns>
    string? ValidateToken(string token);
}