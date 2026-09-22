using System.Collections.Concurrent;
using TriviUpMcp.Client;

namespace TriviUpMcp.Auth;

/// <summary>
/// Almacén singleton de tokens por sesión (clave = <see cref="ISessionKeyProvider.SessionKey"/>).
/// Es singleton a propósito: el DI "Scoped" de ASP.NET Core vive por cada request HTTP individual,
/// no por toda la sesión MCP (que son varios requests), así que no sirve para persistir el login
/// entre llamadas. Este diccionario, indexado explícitamente por el id de sesión, sí persiste.
/// </summary>
public class SessionAuthStore
{
    private readonly ConcurrentDictionary<string, (string Token, UserDto User)> _sessions = new();

    public void Set(string key, string token, UserDto user) => _sessions[key] = (token, user);

    public (string Token, UserDto User)? Get(string key) =>
        _sessions.TryGetValue(key, out var value) ? value : null;

    public void Clear(string key) => _sessions.TryRemove(key, out _);
}
