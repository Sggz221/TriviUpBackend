using System.Collections.Concurrent;

namespace TriviUpMcpServer.OAuth;

/// <summary>
/// Estado en memoria del "Authorization Server" mínimo: clientes registrados dinámicamente
/// y códigos de autorización pendientes de canjear. No guarda access tokens: el access token
/// que emitimos ES el JWT de TriviUp (ver TriviUpBearerAuthenticationHandler), así que no hay
/// nada más que persistir para ellos — su validez y expiración ya las gestiona TriviUp.
/// </summary>
public class OAuthStore
{
    private readonly ConcurrentDictionary<string, ClientRegistration> _clients = new();
    private readonly ConcurrentDictionary<string, AuthorizationCode> _codes = new();

    public ClientRegistration RegisterClient(List<string> redirectUris, string clientName)
    {
        var client = new ClientRegistration(
            ClientId: Guid.NewGuid().ToString("N"),
            RedirectUris: redirectUris,
            ClientName: clientName);
        _clients[client.ClientId] = client;
        return client;
    }

    public ClientRegistration? TryGetClient(string clientId) =>
        _clients.TryGetValue(clientId, out var client) ? client : null;

    public string StoreCode(AuthorizationCode code)
    {
        var value = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        _codes[value] = code;
        return value;
    }

    /// <summary>Recupera y BORRA el código (de un solo uso). Null si no existe o ya caducó.</summary>
    public AuthorizationCode? TryConsumeCode(string code)
    {
        if (!_codes.TryRemove(code, out var value)) return null;
        return value.ExpiresAt > DateTimeOffset.UtcNow ? value : null;
    }
}
