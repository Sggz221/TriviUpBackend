using TriviUpMcp.Client;

namespace TriviUpMcp.Auth;

/// <summary>
/// Fachada por-sesión sobre <see cref="SessionAuthStore"/>: expone el token/usuario de la
/// sesión actual (según <see cref="ISessionKeyProvider"/>) con la misma API de siempre, para
/// que las tools y <see cref="TriviUpMcp.Client.TriviUpApiClient"/> no necesiten saber nada
/// sobre sesiones ni sobre el almacén subyacente.
/// </summary>
public class AuthSessionState(SessionAuthStore store, ISessionKeyProvider keyProvider)
{
    public bool IsAuthenticated => store.Get(keyProvider.SessionKey) is not null;

    public UserDto? CurrentUser => store.Get(keyProvider.SessionKey)?.User;

    public void SetSession(string token, UserDto user) => store.Set(keyProvider.SessionKey, token, user);

    public void Clear() => store.Clear(keyProvider.SessionKey);

    public string? GetTokenOrNull() => store.Get(keyProvider.SessionKey)?.Token;
}
