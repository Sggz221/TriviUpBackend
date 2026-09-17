using System.Text.Json.Serialization;

namespace TriviUpBackend.DTO.User;

/// <summary>
/// DTO con información pública del usuario.
/// </summary>
public record UserDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("profilePhotoUrl")] string? ProfilePhotoUrl
);