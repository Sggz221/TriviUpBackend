using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TriviUpBackend.Common.Storage;
using TriviUpBackend.DTO.User;
using TriviUpBackend.Models.Auth;
using TriviUpBackend.Repositories.Users;
using TriviUpBackend.Services.Auth;

namespace TriviUpBackend.Controllers;

/// <summary>
/// Controlador de gestión de usuarios.
/// Proporciona endpoints para administración de usuarios y gestión del perfil propio.
/// </summary>
[ApiController]
[Route("[controller]")]
[Produces("application/json")]
[Authorize]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class UsersController(
    IUserRepository userRepository,
    IProfilePhotoStorage profilePhotoStorage,
    IJwtTokenExtractor jwtTokenExtractor,
    ILogger<UsersController> logger
) : ControllerBase
{
    private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

    // =====================================================
    // ENDPOINTS DE ADMINISTRACIÓN (solo accesible para Admins)
    // =====================================================

    /// <summary>
    /// Obtiene todos los usuarios (solo admins).
    /// </summary>
    /// <returns>Lista de usuarios.</returns>
    [HttpGet]
    [Authorize(Roles = UserRoles.ADMIN)]
    [ProducesResponseType(typeof(IEnumerable<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllUsers()
    {
        logger.LogInformation("[GetAllUsers] Solicitando lista de usuarios");

        var users = await userRepository.FindAllAsync();
        var userDtos = users.Select(u => new UserResponseDto(
            u.Id,
            u.Username,
            u.Email,
            u.Role,
            u.CreatedAt,
            u.IsBanned
        ));

        logger.LogInformation("[GetAllUsers] Devolviendo {Count} usuarios", users.Count());
        return Ok(userDtos);
    }

    /// <summary>
    /// Obtiene un usuario por ID (solo admins).
    /// </summary>
    /// <param name="id">ID del usuario.</param>
    /// <returns>Información del usuario.</returns>
    [HttpGet("{id:long}")]
    [Authorize(Roles = UserRoles.ADMIN)]
    [ProducesResponseType(typeof(UserResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserById(long id)
    {
        logger.LogInformation("[GetUserById] Buscando usuario {UserId}", id);

        var user = await userRepository.FindByIdAsync(id);
        if (user is null)
        {
            logger.LogWarning("[GetUserById] Usuario {UserId} no encontrado", id);
            return NotFound(new { message = "Usuario no encontrado" });
        }

        var userDto = new UserResponseDto(
            user.Id,
            user.Username,
            user.Email,
            user.Role,
            user.CreatedAt,
            user.IsBanned
        );

        return Ok(userDto);
    }

    /// <summary>
    /// Actualiza un usuario (solo admins).
    /// Permite cambiar: username, email, role.
    /// </summary>
    /// <param name="id">ID del usuario a actualizar.</param>
    /// <param name="dto">Campos a actualizar.</param>
    /// <returns>Usuario actualizado.</returns>
    [HttpPut("{id:long}")]
    [Authorize(Roles = UserRoles.ADMIN)]
    [ProducesResponseType(typeof(UserResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUser(long id, [FromBody] AdminUpdateUserDto dto)
    {
        logger.LogInformation("[UpdateUser] Actualizando usuario {UserId}", id);

        var user = await userRepository.FindByIdAsync(id);
        if (user is null)
        {
            logger.LogWarning("[UpdateUser] Usuario {UserId} no encontrado", id);
            return NotFound(new { message = "Usuario no encontrado" });
        }

        var messages = new List<string>();

        // Validar y actualizar username si se provee
        if (!string.IsNullOrEmpty(dto.Username))
        {
            var usernameValidation = ValidateUsername(dto.Username);
            if (usernameValidation.IsFailure)
            {
                return BadRequest(new { message = usernameValidation.Error });
            }

            // Verificar que el username no esté en uso por otro usuario
            var existingUser = await userRepository.FindByUsernameAsync(dto.Username);
            if (existingUser is not null && existingUser.Id != id)
            {
                return Conflict(new { message = "El nombre de usuario ya está en uso" });
            }

            if (user.Username != dto.Username)
            {
                user.Username = dto.Username;
                messages.Add("username actualizado");
            }
        }

        // Validar y actualizar email si se provee
        if (!string.IsNullOrEmpty(dto.Email))
        {
            var emailValidation = ValidateEmail(dto.Email);
            if (emailValidation.IsFailure)
            {
                return BadRequest(new { message = emailValidation.Error });
            }

            // Verificar que el email no esté en uso por otro usuario
            var existingEmail = await userRepository.FindByEmailAsync(dto.Email);
            if (existingEmail is not null && existingEmail.Id != id)
            {
                return Conflict(new { message = "El correo electrónico ya está en uso" });
            }

            if (user.Email != dto.Email)
            {
                user.Email = dto.Email;
                messages.Add("email actualizado");
            }
        }

        // Validar y actualizar role si se provee
        if (!string.IsNullOrEmpty(dto.Role))
        {
            var roleValidation = ValidateRole(dto.Role);
            if (roleValidation.IsFailure)
            {
                return BadRequest(new { message = roleValidation.Error });
            }

            if (user.Role != dto.Role)
            {
                user.Role = dto.Role;
                messages.Add("role actualizado");
            }
        }

        // Si no hay cambios, retornar éxito sin actualizar
        if (messages.Count == 0)
        {
            return Ok(new UserResponseDto(
                user.Id,
                user.Username,
                user.Email,
                user.Role,
                user.CreatedAt,
                user.IsBanned
            ));
        }

        await userRepository.UpdateAsync(user);

        logger.LogInformation("[UpdateUser] Usuario {UserId} actualizado: {Changes}", id, string.Join(", ", messages));

        return Ok(new UserResponseDto(
            user.Id,
            user.Username,
            user.Email,
            user.Role,
            user.CreatedAt,
            user.IsBanned
        ));
    }

    /// <summary>
    /// Banea a un usuario (solo admins).
    /// Un admin no puede banearse a sí mismo.
    /// </summary>
    /// <param name="id">ID del usuario a banear.</param>
    /// <returns>Mensaje de éxito.</returns>
    [HttpPost("{id:long}/ban")]
    [Authorize(Roles = UserRoles.ADMIN)]
    [ProducesResponseType(typeof(BanUserResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BanUser(long id)
    {
        logger.LogInformation("[BanUser] Intentando banear usuario {UserId}", id);

        var user = await userRepository.FindByIdAsync(id);
        if (user is null)
        {
            logger.LogWarning("[BanUser] Usuario {UserId} no encontrado", id);
            return NotFound(new { message = "Usuario no encontrado" });
        }

        // Verificar que no se puede banear a sí mismo
        var token = ExtractTokenFromRequest();
        if (token is null)
        {
            logger.LogWarning("[BanUser] Token no proporcionado");
            return Unauthorized(new { message = "Token no proporcionado" });
        }

        var currentUserId = jwtTokenExtractor.ExtractUserId(token);
        if (currentUserId == id)
        {
            logger.LogWarning("[BanUser] Admin {AdminId} intentó banearse a sí mismo", id);
            return BadRequest(new { message = "No puedes banearte a ti mismo" });
        }

        user.IsBanned = true;
        await userRepository.UpdateAsync(user);

        logger.LogInformation("[BanUser] Usuario {UserId} ha sido baneado por admin", id);
        return Ok(new BanUserResponseDto("Usuario baneado correctamente"));
    }

    /// <summary>
    /// Activa a un usuario baneado (solo admins).
    /// </summary>
    /// <param name="id">ID del usuario a activar.</param>
    /// <returns>Mensaje de éxito.</returns>
    [HttpPost("{id:long}/activate")]
    [Authorize(Roles = UserRoles.ADMIN)]
    [ProducesResponseType(typeof(BanUserResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateUser(long id)
    {
        logger.LogInformation("[ActivateUser] Intentando activar usuario {UserId}", id);

        var user = await userRepository.FindByIdAsync(id);
        if (user is null)
        {
            logger.LogWarning("[ActivateUser] Usuario {UserId} no encontrado", id);
            return NotFound(new { message = "Usuario no encontrado" });
        }

        user.IsBanned = false;
        await userRepository.UpdateAsync(user);

        logger.LogInformation("[ActivateUser] Usuario {UserId} ha sido activado por admin", id);
        return Ok(new BanUserResponseDto("Usuario activado correctamente"));
    }

    // =====================================================
    // ENDPOINTS DE PERFIL DE USUARIO
    // =====================================================

    /// <summary>
    /// Actualiza la foto de perfil del usuario autenticado.
    /// </summary>
    /// <param name="dto">DTO con el archivo de imagen.</param>
    /// <returns>URL completa de la nueva foto de perfil.</returns>
    [HttpPut("profile-photo")]
    [ProducesResponseType(typeof(ProfilePhotoResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> UpdateProfilePhoto([FromForm] UpdateProfilePhotoDto dto)
    {
        logger.LogInformation("[UpdateProfilePhoto] Inicio");
        logger.LogInformation("[UpdateProfilePhoto] Content-Type: {ContentType}", Request.ContentType);
        logger.LogInformation("[UpdateProfilePhoto] dto: {@Dto}", dto);
        logger.LogInformation("[UpdateProfilePhoto] dto.File: {File}", dto?.File?.FileName ?? "NULL");

        var token = ExtractTokenFromRequest();
        if (token is null)
        {
            logger.LogWarning("Intento de actualizar foto de perfil sin token");
            return Unauthorized(new { message = "Token no proporcionado" });
        }

        var userId = jwtTokenExtractor.ExtractUserId(token);
        if (userId is null)
        {
            logger.LogWarning("Intento de actualizar foto de perfil sin token válido");
            return Unauthorized(new { message = "Token inválido o expirado" });
        }

        if (dto.File is null || dto.File.Length == 0)
        {
            logger.LogWarning("[UpdateProfilePhoto] Archivo nulo o vacío");
            return BadRequest(new { message = "No se ha proporcionado ningún archivo" });
        }

        if (dto.File.Length > MaxFileSize)
        {
            logger.LogWarning("[UpdateProfilePhoto] Archivo demasiado grande: {Size}", dto.File.Length);
            return BadRequest(new { message = "El archivo excede el tamaño máximo permitido de 5MB" });
        }

        var user = await userRepository.FindByIdAsync(userId.Value);
        if (user is null)
        {
            logger.LogWarning("Usuario no encontrado: {UserId}", userId);
            return NotFound(new { message = "Usuario no encontrado" });
        }

        if (!string.IsNullOrEmpty(user.ProfilePhotoUrl))
        {
            logger.LogInformation("[UpdateProfilePhoto] Eliminando foto anterior: {Url}", user.ProfilePhotoUrl);
            var deleteResult = await profilePhotoStorage.DeleteProfilePhotoAsync(user.ProfilePhotoUrl);
            if (deleteResult.IsFailure)
            {
                logger.LogWarning("No se pudo eliminar la foto de perfil anterior: {Error}", deleteResult.Error);
            }
        }

        logger.LogInformation("[UpdateProfilePhoto] Guardando nueva foto para usuario {UserId}", userId);
        var saveResult = await profilePhotoStorage.SaveProfilePhotoAsync(userId.Value, dto.File);
        if (saveResult.IsFailure)
        {
            logger.LogError("Error guardando foto de perfil: {Error}", saveResult.Error);
            return BadRequest(new { message = saveResult.Error });
        }

        user.ProfilePhotoUrl = saveResult.Value;
        await userRepository.UpdateAsync(user);

        var fullUrl = profilePhotoStorage.GetFullUrl(saveResult.Value);

        logger.LogInformation("Foto de perfil actualizada para usuario {UserId}: {Url}", userId, fullUrl);

        var response = new ProfilePhotoResponseDto(fullUrl);
        return Ok(response);
    }

    /// <summary>
    /// Elimina la foto de perfil del usuario autenticado.
    /// </summary>
    /// <returns>Mensaje de éxito.</returns>
    [HttpDelete("profile-photo")]
    [ProducesResponseType(typeof(ProfilePhotoDeleteResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProfilePhoto()
    {
        var token = ExtractTokenFromRequest();
        if (token is null)
        {
            logger.LogWarning("Intento de eliminar foto de perfil sin token");
            return Unauthorized(new { message = "Token no proporcionado" });
        }

        var userId = jwtTokenExtractor.ExtractUserId(token);
        if (userId is null)
        {
            logger.LogWarning("Intento de eliminar foto de perfil sin token válido");
            return Unauthorized(new { message = "Token inválido o expirado" });
        }

        var user = await userRepository.FindByIdAsync(userId.Value);
        if (user is null)
        {
            logger.LogWarning("Usuario no encontrado: {UserId}", userId);
            return NotFound(new { message = "Usuario no encontrado" });
        }

        if (string.IsNullOrEmpty(user.ProfilePhotoUrl))
        {
            return Ok(new ProfilePhotoDeleteResponseDto("No se encontró foto de perfil para eliminar"));
        }

        var deleteResult = await profilePhotoStorage.DeleteProfilePhotoAsync(user.ProfilePhotoUrl);
        if (deleteResult.IsFailure)
        {
            logger.LogWarning("No se pudo eliminar la foto de perfil: {Error}", deleteResult.Error);
            return BadRequest(new { message = deleteResult.Error });
        }

        user.ProfilePhotoUrl = null;
        await userRepository.UpdateAsync(user);

        logger.LogInformation("Foto de perfil eliminada para usuario {UserId}", userId);

        return Ok(new ProfilePhotoDeleteResponseDto("Foto de perfil eliminada correctamente"));
    }

    /// <summary>
    /// Obtiene la información del usuario autenticado.
    /// </summary>
    /// <returns>Información del usuario.</returns>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser()
    {
        var token = ExtractTokenFromRequest();
        if (token is null)
        {
            return Unauthorized(new { message = "Token no proporcionado" });
        }

        var userId = jwtTokenExtractor.ExtractUserId(token);
        if (userId is null)
        {
            return Unauthorized(new { message = "Token inválido o expirado" });
        }

        var user = await userRepository.FindByIdAsync(userId.Value);
        if (user is null)
        {
            return NotFound(new { message = "Usuario no encontrado" });
        }

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

        return Ok(userDto);
    }

    /// <summary>
    /// Actualiza el perfil del usuario autenticado.
    /// </summary>
    /// <param name="dto">DTO con los campos a actualizar.</param>
    /// <returns>Usuario actualizado.</returns>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UpdatedUserResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCurrentUser([FromBody] UpdateUserDto dto)
    {
        var token = ExtractTokenFromRequest();
        if (token is null)
        {
            return Unauthorized(new { message = "Token no proporcionado" });
        }

        var userId = jwtTokenExtractor.ExtractUserId(token);
        if (userId is null)
        {
            return Unauthorized(new { message = "Token inválido o expirado" });
        }

        var user = await userRepository.FindByIdAsync(userId.Value);
        if (user is null)
        {
            return NotFound(new { message = "Usuario no encontrado" });
        }

        var messages = new List<string>();

        // Validar y actualizar username si se provee
        if (!string.IsNullOrEmpty(dto.Username))
        {
            var usernameValidation = ValidateUsername(dto.Username);
            if (usernameValidation.IsFailure)
            {
                return BadRequest(new { message = usernameValidation.Error });
            }

            // Verificar que el username no esté en uso por otro usuario
            var existingUser = await userRepository.FindByUsernameAsync(dto.Username);
            if (existingUser is not null && existingUser.Id != userId)
            {
                return Conflict(new { message = "El nombre de usuario ya está en uso" });
            }

            if (user.Username != dto.Username)
            {
                user.Username = dto.Username;
                messages.Add("username actualizado");
            }
        }

        // Validar y actualizar email si se provee
        if (!string.IsNullOrEmpty(dto.Email))
        {
            var emailValidation = ValidateEmail(dto.Email);
            if (emailValidation.IsFailure)
            {
                return BadRequest(new { message = emailValidation.Error });
            }

            // Verificar que el email no esté en uso por otro usuario
            var existingEmail = await userRepository.FindByEmailAsync(dto.Email);
            if (existingEmail is not null && existingEmail.Id != userId)
            {
                return Conflict(new { message = "El correo electrónico ya está en uso" });
            }

            if (user.Email != dto.Email)
            {
                user.Email = dto.Email;
                messages.Add("email actualizado");
            }
        }

        // Validar y actualizar password si se provee
        if (!string.IsNullOrEmpty(dto.Password))
        {
            var passwordValidation = ValidatePassword(dto.Password);
            if (passwordValidation.IsFailure)
            {
                return BadRequest(new { message = passwordValidation.Error });
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 11);
            messages.Add("password actualizado");
        }

        // Si no hay cambios, retornar éxito sin actualizar
        if (messages.Count == 0)
        {
            return Ok(new UpdatedUserResponseDto(
                MapToUserDto(user),
                "No se realizaron cambios"
            ));
        }

        await userRepository.UpdateAsync(user);

        logger.LogInformation("Perfil actualizado para usuario {UserId}: {Changes}",
            userId, string.Join(", ", messages));

        return Ok(new UpdatedUserResponseDto(
            MapToUserDto(user),
            $"Perfil actualizado: {string.Join(", ", messages)}"
        ));
    }

    /// <summary>
    /// Extrae el token JWT del header Authorization.
    /// </summary>
    private string? ExtractTokenFromRequest()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader))
        {
            return null;
        }

        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        return authHeader;
    }

    /// <summary>
    /// Valida el formato del username.
    /// </summary>
    private static Result<bool, string> ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Result.Failure<bool, string>("El nombre de usuario no puede estar vacío");
        }

        if (username.Length < 3 || username.Length > 50)
        {
            return Result.Failure<bool, string>("El nombre de usuario debe tener entre 3 y 50 caracteres");
        }

        if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
        {
            return Result.Failure<bool, string>("El nombre de usuario solo puede contener letras, números y guiones bajos");
        }

        return Result.Success<bool, string>(true);
    }

    /// <summary>
    /// Valida el formato del email.
    /// </summary>
    private static Result<bool, string> ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Result.Failure<bool, string>("El correo electrónico no puede estar vacío");
        }

        if (email.Length > 100)
        {
            return Result.Failure<bool, string>("El correo electrónico no puede exceder 100 caracteres");
        }

        // Validar formato básico de email con regex
        var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        if (!emailRegex.IsMatch(email))
        {
            return Result.Failure<bool, string>("Debe ser un correo electrónico válido");
        }

        return Result.Success<bool, string>(true);
    }

    /// <summary>
    /// Valida el password.
    /// </summary>
    private static Result<bool, string> ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return Result.Failure<bool, string>("La contraseña no puede estar vacía");
        }

        if (password.Length < 4)
        {
            return Result.Failure<bool, string>("La contraseña debe tener al menos 4 caracteres");
        }

        if (password.Length > 100)
        {
            return Result.Failure<bool, string>("La contraseña no puede exceder 100 caracteres");
        }

        return Result.Success<bool, string>(true);
    }

    /// <summary>
    /// Valida el rol del usuario.
    /// </summary>
    private static Result<bool, string> ValidateRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return Result.Failure<bool, string>("El rol no puede estar vacío");
        }

        if (role != UserRoles.USER && role != UserRoles.ADMIN)
        {
            return Result.Failure<bool, string>($"Rol inválido. Debe ser '{UserRoles.USER}' o '{UserRoles.ADMIN}'");
        }

        return Result.Success<bool, string>(true);
    }

    /// <summary>
    /// Mapea un usuario a UserDto incluyendo la URL completa de la foto de perfil.
    /// </summary>
    private UserDto MapToUserDto(User user)
    {
        var profilePhotoUrl = !string.IsNullOrEmpty(user.ProfilePhotoUrl)
            ? profilePhotoStorage.GetFullUrl(user.ProfilePhotoUrl)
            : null;

        return new UserDto(
            user.Id,
            user.Username,
            user.Email,
            user.Role,
            user.CreatedAt,
            profilePhotoUrl
        );
    }
}

/// <summary>
/// Respuesta DTO para la actualización de foto de perfil.
/// </summary>
public record ProfilePhotoResponseDto(
    [property: JsonPropertyName("profilePhotoUrl")] string ProfilePhotoUrl
);

/// <summary>
/// Respuesta DTO para la eliminación de foto de perfil.
/// </summary>
public record ProfilePhotoDeleteResponseDto(
    [property: JsonPropertyName("message")] string Message
);

/// <summary>
/// Respuesta DTO para el banneo de usuario.
/// </summary>
public record BanUserResponseDto(
    [property: JsonPropertyName("message")] string Message
);