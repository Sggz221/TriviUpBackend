using System.Security.Claims;

namespace TriviUpBackend.Services.Auth;

/// <summary>
/// Interfaz para extraer información de tokens JWT.
/// Proporciona métodos para extraer claims y validar tokens.
/// </summary>
public interface IJwtTokenExtractor
{
    /// <summary>
    /// Extrae el ID de usuario de un token JWT.
    /// </summary>
    /// <param name="token">Token JWT.</param>
    /// <returns>ID del usuario o null si no se puede extraer.</returns>
    long? ExtractUserId(string token);

    /// <summary>
    /// Extrae el rol de un token JWT.
    /// </summary>
    /// <param name="token">Token JWT.</param>
    /// <returns>Nombre del rol o null si no se puede extraer.</returns>
    string? ExtractRole(string token);

    /// <summary>
    /// Determina si el token pertenece a un administrador.
    /// </summary>
    /// <param name="token">Token JWT.</param>
    /// <returns>True si el usuario es admin, false en caso contrario.</returns>
    bool IsAdmin(string token);

    /// <summary>
    /// Extrae información completa del usuario del token.
    /// </summary>
    /// <param name="token">Token JWT.</param>
    /// <returns>Tupla con UserId, IsAdmin y Role.</returns>
    (long? UserId, bool IsAdmin, string? Role) ExtractUserInfo(string token);

    /// <summary>
    /// Extrae todos los claims del token JWT.
    /// </summary>
    /// <param name="token">Token JWT.</param>
    /// <returns>ClaimsPrincipal con los claims del token o null si falla.</returns>
    ClaimsPrincipal? ExtractClaims(string token);

    /// <summary>
    /// Extrae el email del token JWT.
    /// </summary>
    /// <param name="token">Token JWT.</param>
    /// <returns>Email del usuario o null si no se puede extraer.</returns>
    string? ExtractEmail(string token);

    /// <summary>
    /// Valida que el formato del token sea un JWT válido (3 partes separadas por puntos).
    /// </summary>
    /// <param name="token">Token a validar.</param>
    /// <returns>True si el formato es válido, false en caso contrario.</returns>
    bool IsValidTokenFormat(string token);
}