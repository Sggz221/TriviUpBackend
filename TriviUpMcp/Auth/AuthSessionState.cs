using TriviUpMcp.Client;

namespace TriviUpMcp.Auth;

/// <summary>
/// Sesión del proceso MCP: guarda el JWT obtenido por la tool "login" en memoria
/// para que el resto de tools lo reutilicen como Bearer token. Un proceso MCP (stdio)
/// atiende a un único cliente, así que un solo token en memoria es suficiente.
/// </summary>
public class AuthSessionState
{
    private readonly object _lock = new();
    private string? _token;
    private UserDto? _user;

    public bool IsAuthenticated
    {
        get { lock (_lock) return _token is not null; }
    }

    public UserDto? CurrentUser
    {
        get { lock (_lock) return _user; }
    }

    public void SetSession(string token, UserDto user)
    {
        lock (_lock)
        {
            _token = token;
            _user = user;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _token = null;
            _user = null;
        }
    }

    public string? GetTokenOrNull()
    {
        lock (_lock) return _token;
    }
}
