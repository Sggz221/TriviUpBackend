namespace TriviUpMcpServer;

/// <summary>Deriva la clave de sesión (para <c>SessionAuthStore</c>) a partir de un HttpContext.
/// Usado tanto por <see cref="HttpSessionKeyProvider"/> (para las tools) como por
/// <see cref="TriviUpBearerAuthenticationHandler"/> (para sembrar la sesión al validar un
/// token OAuth), así que vive en un único sitio.</summary>
public static class McpSessionKey
{
    public const string SessionIdHeader = "Mcp-Session-Id";

    public static string From(HttpContext? context) =>
        context?.Request.Headers[SessionIdHeader].FirstOrDefault()
        ?? context?.TraceIdentifier
        ?? Guid.NewGuid().ToString();
}
