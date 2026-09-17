using System.Text.Json.Serialization;

namespace TriviUpBackend.DTO.User;

/// <summary>
/// DTO de respuesta tras un proceso de autenticación exitoso.
/// Contiene el token JWT y los datos del usuario.
/// </summary>
public record AuthResponseDto(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("user")] UserDto User
);