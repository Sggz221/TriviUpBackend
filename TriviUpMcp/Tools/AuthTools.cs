using System.ComponentModel;
using ModelContextProtocol.Server;
using TriviUpMcp.Auth;
using TriviUpMcp.Client;

namespace TriviUpMcp.Tools;

[McpServerToolType]
public class AuthTools(TriviUpApiClient client, AuthSessionState session)
{
    [McpServerTool(Name = "login")]
    [Description("Inicia sesión contra la API de TriviUp con usuario y contraseña, y guarda el token en la sesión del MCP. " +
                 "Debe llamarse antes que cualquier otra tool (whoami, crear_pregunta, etc.), ya que todas requieren autenticación.")]
    public Task<string> Login(
        [Description("Nombre de usuario o email registrado en TriviUp")] string username,
        [Description("Contraseña del usuario")] string password,
        CancellationToken ct)
    {
        return ToolExecution.RunAsync(async () =>
        {
            var auth = await client.SignInAsync(username, password, ct);
            session.SetSession(auth.Token, auth.User);
            return new { message = "Sesión iniciada correctamente.", user = auth.User };
        });
    }

    [McpServerTool(Name = "whoami")]
    [Description("Devuelve la información del usuario actualmente autenticado en el MCP (id, username, email, rol).")]
    public Task<string> Whoami(CancellationToken ct)
    {
        return ToolExecution.RunAsync(() => client.GetMeAsync(ct));
    }
}
