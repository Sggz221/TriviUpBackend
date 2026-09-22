namespace TriviUpMcp.Auth;

/// <summary>
/// Identifica a qué "sesión lógica" pertenece la llamada actual a una tool, para que
/// <see cref="SessionAuthStore"/> sepa en qué cajón guardar/leer el token de cada usuario.
/// stdio: un proceso = un cliente = una clave fija. HTTP: una clave por sesión MCP
/// (cabecera Mcp-Session-Id), para que el conector pueda atender a varios usuarios a la vez
/// sin que se mezclen sus tokens.
/// </summary>
public interface ISessionKeyProvider
{
    string SessionKey { get; }
}

/// <summary>Implementación para el host stdio: un único proceso por cliente, clave constante.</summary>
public class StdioSessionKeyProvider : ISessionKeyProvider
{
    public string SessionKey => "stdio";
}
