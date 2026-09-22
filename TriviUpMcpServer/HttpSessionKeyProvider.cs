using TriviUpMcp.Auth;

namespace TriviUpMcpServer;

/// <summary>
/// Identifica la sesión actual por la cabecera Mcp-Session-Id que pone el SDK en modo
/// stateful. Si por lo que sea no hay sesión (petición suelta sin handshake 'initialize',
/// o cliente en modo totalmente stateless), cada request cae a una clave distinta: no hay
/// "sesión" real que mantener y toca hacer login en cada llamada, pero nunca se comparte
/// token entre peticiones de usuarios distintos.
/// </summary>
public class HttpSessionKeyProvider(IHttpContextAccessor accessor) : ISessionKeyProvider
{
    private const string SessionIdHeader = "Mcp-Session-Id";

    public string SessionKey =>
        accessor.HttpContext?.Request.Headers[SessionIdHeader].FirstOrDefault()
        ?? accessor.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString();
}
