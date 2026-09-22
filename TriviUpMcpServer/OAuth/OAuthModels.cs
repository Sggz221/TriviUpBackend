using System.Text.Json.Serialization;

namespace TriviUpMcpServer.OAuth;

/// <summary>Cliente registrado dinámicamente (RFC 7591). Público: sin client_secret, usa PKCE.</summary>
public record ClientRegistration(string ClientId, List<string> RedirectUris, string ClientName);

/// <summary>Código de autorización de un solo uso (2 min), ligado a PKCE y al JWT ya obtenido de TriviUp.</summary>
public record AuthorizationCode(
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string TriviUpJwt,
    DateTimeOffset ExpiresAt
);

/// <summary>Correlación de un solo uso (5 min) para el viaje de ida y vuelta por Google: guarda
/// la petición OAuth original (de claude.ai u otro cliente) mientras el usuario está en
/// accounts.google.com, ya que el 'state' de Google lo usa internamente TriviUpBackend para su
/// propio returnUrl y no podemos reutilizarlo para nuestro propio state.</summary>
public record PendingGoogleLogin(
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string? State,
    DateTimeOffset ExpiresAt
);

/// <summary>URL base del backend de TriviUp, para construir el link a /Auth/google.</summary>
public record TriviUpBackendOptions(string BaseUrl);

// ---- DTOs de las peticiones/respuestas HTTP ----

public record ClientRegistrationRequest(
    [property: JsonPropertyName("redirect_uris")] List<string>? RedirectUris,
    [property: JsonPropertyName("client_name")] string? ClientName,
    [property: JsonPropertyName("token_endpoint_auth_method")] string? TokenEndpointAuthMethod
);

public record ClientRegistrationResponse(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] List<string> RedirectUris,
    [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod,
    [property: JsonPropertyName("grant_types")] List<string> GrantTypes,
    [property: JsonPropertyName("response_types")] List<string> ResponseTypes
);

public record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] long ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken
);

public record OAuthErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription = null
);
