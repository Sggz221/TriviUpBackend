using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TriviUpMcp.Auth;
using TriviUpMcp.Client;

namespace TriviUpMcpServer.OAuth;

/// <summary>
/// "Resource server" del conector: valida el Bearer token de cada request a /mcp llamando a
/// GET /Users/me de TriviUp (el access_token que emitimos en /token ES el JWT de TriviUp, así
/// que no hay nada que validar localmente — delegamos siempre en la fuente de verdad). Si es
/// válido, siembra SessionAuthStore para esta sesión MCP, de forma que las tools ya ven al
/// usuario autenticado sin necesidad de llamar a 'login'.
/// </summary>
public class TriviUpBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TriviUpApiClient apiClient,
    SessionAuthStore sessionAuthStore
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TriviUpBearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(header)
            || !AuthenticationHeaderValue.TryParse(header, out var authHeader)
            || !string.Equals(authHeader.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(authHeader.Parameter))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authHeader.Parameter;

        try
        {
            var user = await apiClient.GetMeWithTokenAsync(token, Context.RequestAborted);

            sessionAuthStore.Set(McpSessionKey.From(Context), token, user);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username)
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }
        catch (TriviUpApiException)
        {
            return AuthenticateResult.Fail("Token inválido o caducado.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate =
            $"Bearer resource_metadata=\"{baseUrl}/.well-known/oauth-protected-resource\"";
        return Task.CompletedTask;
    }
}
