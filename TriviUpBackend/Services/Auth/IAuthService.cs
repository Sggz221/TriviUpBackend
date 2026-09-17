using CSharpFunctionalExtensions;
using TriviUpBackend.DTO.User;
using TriviUpBackend.Errors;

namespace TriviUpBackend.Services.Auth;

/// <summary>
/// Interfaz para el servicio de autenticación.
/// Proporciona métodos para registro, inicio de sesión y autenticación con Google.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registra un nuevo usuario en el sistema.
    /// </summary>
    /// <param name="dto">Datos de registro del usuario.</param>
    /// <returns>Resultado con la respuesta de autenticación o error.</returns>
    Task<Result<AuthResponseDto, AuthError>> SignUpAsync(RegisterDto dto);

    /// <summary>
    /// Inicia sesión con credenciales de usuario.
    /// </summary>
    /// <param name="dto">Datos de inicio de sesión (username y password).</param>
    /// <returns>Resultado con la respuesta de autenticación o error.</returns>
    Task<Result<AuthResponseDto, AuthError>> SignInAsync(LoginDto dto);

    /// <summary>
    /// Inicia sesión o registra un usuario mediante Google OAuth.
    /// </summary>
    /// <param name="googleId">Identificador único de Google del usuario.</param>
    /// <param name="email">Correo electrónico de Google.</param>
    /// <param name="username">Nombre de usuario preferido.</param>
    /// <returns>Resultado con la respuesta de autenticación o error.</returns>
    Task<Result<AuthResponseDto, AuthError>> GoogleSignInAsync(string googleId, string email, string username);
}