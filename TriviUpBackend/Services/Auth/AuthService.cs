using CSharpFunctionalExtensions;
using TriviUpBackend.Common.Storage;
using TriviUpBackend.DTO.User;
using TriviUpBackend.Errors;
using TriviUpBackend.Models.Auth;
using TriviUpBackend.Repositories.Users;

namespace TriviUpBackend.Services.Auth;

/// <summary>
/// Implementación del servicio de autenticación.
/// Gestiona el registro, inicio de sesión y autenticación con Google OAuth.
/// </summary>
public class AuthService(
    IUserRepository userRepository,
    IJwtService jwtService,
    IProfilePhotoStorage profilePhotoStorage,
    ILogger<AuthService> logger
) : IAuthService
{

    
    /// <inheritdoc cref="IAuthService.SignUpAsync"/>
    public async Task<Result<AuthResponseDto, AuthError>> SignUpAsync(RegisterDto dto)
    {
        var sanitizedUsername = dto.Username?.Replace("\n", "").Replace("\r", "");
        logger.LogInformation("SignUp request for username: {Username}", sanitizedUsername);


        var duplicateCheck = await CheckDuplicatesAsync(dto);
        if (duplicateCheck.IsFailure)
        {
            return Result.Failure<AuthResponseDto, AuthError>(duplicateCheck.Error);
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 11);

        var user = new User
        {
            Username = sanitizedUsername!,
            Email = dto.Email!,
            PasswordHash = passwordHash,
            Role = UserRoles.USER,
            IsDeleted = false
        };

        try
        {
            var savedUser = await userRepository.SaveAsync(user);

            // Set LastLoginAt since registration also logs the user in
            savedUser.LastLoginAt = DateTime.UtcNow;
            await userRepository.UpdateAsync(savedUser);

            var authResponse = GenerateAuthResponse(savedUser);

            logger.LogInformation("User registered successfully: {Username}", sanitizedUsername);

            return Result.Success<AuthResponseDto, AuthError>(authResponse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during sign up for username: {Username}", sanitizedUsername);
            return Result.Failure<AuthResponseDto, AuthError>(
                new AuthError("Error en el registro. Por favor, inténtalo de nuevo."));
        }
    }
    
    /// <inheritdoc cref="IAuthService.SignInAsync"/>
    public async Task<Result<AuthResponseDto, AuthError>> SignInAsync(LoginDto dto)
    {
        var sanitizedUsername = dto.Username?.Replace("\n", "").Replace("\r", "");
        logger.LogInformation("[AUTH_SERVICE] SignInAsync START - Username: {Username}", sanitizedUsername);

        try
        {
            logger.LogInformation("[AUTH_SERVICE] About to call FindByUsernameAsync");
            var user = await userRepository.FindByUsernameAsync(dto.Username!);
            logger.LogInformation("[AUTH_SERVICE] FindByUsernameAsync returned: {UserId}", user?.Id);
            if (user is null)
            {
                logger.LogWarning("SignIn fallido: Usuario no encontrado - {Username}", sanitizedUsername);
                return Result.Failure<AuthResponseDto, AuthError>(
                    new AuthUnauthorizedError("Credenciales inválidas")
                );
            }

            var passwordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            if (!passwordValid)
            {
                logger.LogWarning("SignIn fallido: Password inválido - {Username}", sanitizedUsername);
                return Result.Failure<AuthResponseDto, AuthError>(
                    new AuthUnauthorizedError("Credenciales inválidas")
                );
            }

            // Verificar si el usuario está baneado
            if (user.IsBanned)
            {
                logger.LogWarning("[SignIn] Usuario {UserId} ({Email}) intentó iniciar sesión pero está baneado",
                    user.Id, user.Email);
                return Result.Failure<AuthResponseDto, AuthError>(
                    new AuthUnauthorizedError("Tu cuenta ha sido suspendida. Contacta con un administrador.")
                );
            }

            // Actualizar LastLoginAt
            user.LastLoginAt = DateTime.UtcNow;
            await userRepository.UpdateAsync(user);

            var authResponse = GenerateAuthResponse(user);
            logger.LogInformation("Usuario inició sesión correctamente: {Username}", sanitizedUsername);

            return Result.Success<AuthResponseDto, AuthError>(authResponse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during sign in for username: {Username}", sanitizedUsername);
            return Result.Failure<AuthResponseDto, AuthError>(
                new AuthError("Error en el inicio de sesión. Por favor, inténtalo de nuevo."));
        }
    }

    /// <inheritdoc cref="IAuthService.GoogleSignInAsync"/>
    public async Task<Result<AuthResponseDto, AuthError>> GoogleSignInAsync(string googleId, string email, string username)
    {
        logger.LogInformation("Google sign-in attempt for email: {Email}", email);

        try
        {
            var existingByGoogleId = await userRepository.FindByGoogleIdAsync(googleId);
            if (existingByGoogleId is not null)
            {
                existingByGoogleId.LastLoginAt = DateTime.UtcNow;
                await userRepository.UpdateAsync(existingByGoogleId);
                var response = GenerateAuthResponse(existingByGoogleId);
                logger.LogInformation("Google user signed in: {Email}", email);
                return Result.Success<AuthResponseDto, AuthError>(response);
            }

            var existingByEmail = await userRepository.FindByEmailAsync(email);
            if (existingByEmail is not null)
            {
                existingByEmail.GoogleId = googleId;
                existingByEmail.LastLoginAt = DateTime.UtcNow;
                await userRepository.UpdateAsync(existingByEmail);
                var response = GenerateAuthResponse(existingByEmail);
                logger.LogInformation("Linked existing user to Google: {Email}", email);
                return Result.Success<AuthResponseDto, AuthError>(response);
            }

            var user = new User
            {
                Username = username,
                Email = email,
                GoogleId = googleId,
                PasswordHash = string.Empty,
                Role = UserRoles.USER,
                IsDeleted = false,
                LastLoginAt = DateTime.UtcNow
            };

            var savedUser = await userRepository.SaveAsync(user);
            var authResponse = GenerateAuthResponse(savedUser);
            logger.LogInformation("New user created via Google sign-in: {Email}", email);

            return Result.Success<AuthResponseDto, AuthError>(authResponse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Google sign-in for email: {Email}", email);
            return Result.Failure<AuthResponseDto, AuthError>(
                new AuthError("Error en el inicio de sesión con Google. Por favor, inténtalo de nuevo."));
        }
    }
    
    /// <summary>
    /// Verifica que el username y email no estén en uso.
    /// </summary>
    /// <param name="dto">Datos de registro a verificar.</param>
    /// <returns>Éxito si no hay duplicados, error de conflicto en caso contrario.</returns>
    private async Task<UnitResult<AuthError>> CheckDuplicatesAsync(RegisterDto dto)
    {
        var existingUser = await userRepository.FindByUsernameAsync(dto.Username!);
        if (existingUser is not null)
        {
            return UnitResult.Failure<AuthError>(new AuthConflictError("username ya en uso: " + existingUser.Username));
        }

        var existingEmail = await userRepository.FindByEmailAsync(dto.Email!);
        if (existingEmail is not null)
        {
            return UnitResult.Failure<AuthError>(new AuthConflictError("email ya en uso: " + existingEmail.Email));
        }

        return UnitResult.Success<AuthError>();
    }

    
    /// <summary>
    /// Genera la respuesta de autenticación con token JWT y datos del usuario.
    /// </summary>
    /// <param name="user">Usuario para el cual generar la respuesta.</param>
    /// <returns>DTO de respuesta de autenticación.</returns>
    private AuthResponseDto GenerateAuthResponse(User user)
    {
        var token = jwtService.GenerateToken(user);

        var profilePhotoUrl = !string.IsNullOrEmpty(user.ProfilePhotoUrl)
            ? profilePhotoStorage.GetFullUrl(user.ProfilePhotoUrl)
            : null;

        var userDto = new UserDto(
            user.Id,
            user.Username,
            user.Email,
            user.Role,
            user.CreatedAt,
            profilePhotoUrl
        );

        return new AuthResponseDto(token, userDto);
    }
}