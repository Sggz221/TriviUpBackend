using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.WebUtilities;
using TriviUpMcp.Client;

namespace TriviUpMcpServer.OAuth;

/// <summary>
/// "Authorization Server" mínimo (RFC 8414 + RFC 7591 + Authorization Code con PKCE) que hace
/// de puente hacia el login/JWT que ya tiene TriviUp. No es un servidor OAuth de propósito
/// general: existe únicamente para que clientes MCP (claude.ai, Claude Desktop/Code) puedan
/// registrarse y autenticar a un usuario de TriviUp mediante el flujo estándar que esos
/// clientes ya saben hablar, en vez de depender de una tool 'login' dentro de la conversación.
///
/// El access_token que emitimos ES el JWT de TriviUp: no hay tokens opacos que mantener
/// sincronizados, y la validación en cada request delega en la propia API de TriviUp
/// (ver TriviUpBearerAuthenticationHandler).
/// </summary>
public static class OAuthEndpoints
{
    private const int CodeTtlSeconds = 120;

    public static void MapTriviUpOAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx) =>
        {
            var baseUrl = BaseUrl(ctx);
            return Results.Json(new
            {
                resource = $"{baseUrl}/mcp",
                authorization_servers = new[] { baseUrl }
            });
        });

        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext ctx) =>
        {
            var baseUrl = BaseUrl(ctx);
            return Results.Json(new
            {
                issuer = baseUrl,
                authorization_endpoint = $"{baseUrl}/authorize",
                token_endpoint = $"{baseUrl}/token",
                registration_endpoint = $"{baseUrl}/register",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                code_challenge_methods_supported = new[] { "S256" },
                token_endpoint_auth_methods_supported = new[] { "none" }
            });
        });

        app.MapPost("/register", (ClientRegistrationRequest body, OAuthStore store) =>
        {
            var redirectUris = body.RedirectUris?.Where(u => Uri.TryCreate(u, UriKind.Absolute, out _)).ToList();
            if (redirectUris is null || redirectUris.Count == 0)
            {
                return Results.BadRequest(new OAuthErrorResponse("invalid_client_metadata", "redirect_uris es obligatorio."));
            }

            var client = store.RegisterClient(redirectUris, body.ClientName ?? "MCP client");

            return Results.Json(new ClientRegistrationResponse(
                ClientId: client.ClientId,
                ClientName: client.ClientName,
                RedirectUris: client.RedirectUris,
                TokenEndpointAuthMethod: "none",
                GrantTypes: ["authorization_code", "refresh_token"],
                ResponseTypes: ["code"]
            ), statusCode: StatusCodes.Status201Created);
        });

        app.MapGet("/authorize", (HttpContext ctx, OAuthStore store) =>
        {
            var q = ctx.Request.Query;
            var error = ValidateAuthorizeRequest(q, store, out var client);
            if (error is not null) return error;

            return Results.Content(RenderLoginPage(q["client_id"]!, q["redirect_uri"]!, q["state"],
                q["code_challenge"]!, client!.ClientName, error: null), "text/html");
        });

        app.MapPost("/authorize", async (HttpContext ctx, OAuthStore store, TriviUpApiClient apiClient) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            string? Get(string key) => form.TryGetValue(key, out var v) ? v.ToString() : null;

            var clientId = Get("client_id") ?? "";
            var redirectUri = Get("redirect_uri") ?? "";
            var state = Get("state");
            var codeChallenge = Get("code_challenge") ?? "";
            var username = Get("username") ?? "";
            var password = Get("password") ?? "";

            var client = store.TryGetClient(clientId);
            if (client is null || !client.RedirectUris.Contains(redirectUri))
            {
                return Results.BadRequest(new OAuthErrorResponse("invalid_request", "Cliente o redirect_uri inválidos."));
            }

            try
            {
                var auth = await apiClient.SignInAsync(username, password, ctx.RequestAborted);

                var code = store.StoreCode(new AuthorizationCode(
                    ClientId: clientId,
                    RedirectUri: redirectUri,
                    CodeChallenge: codeChallenge,
                    TriviUpJwt: auth.Token,
                    ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(CodeTtlSeconds)));

                var queryParams = new Dictionary<string, string?> { ["code"] = code };
                if (!string.IsNullOrEmpty(state)) queryParams["state"] = state;
                var redirect = QueryHelpers.AddQueryString(redirectUri, queryParams);

                return Results.Redirect(redirect);
            }
            catch (TriviUpApiException)
            {
                return Results.Content(RenderLoginPage(clientId, redirectUri, state, codeChallenge, client.ClientName,
                    error: "Usuario o contraseña incorrectos."), "text/html");
            }
        });

        app.MapPost("/token", async (HttpContext ctx, OAuthStore store, TriviUpApiClient apiClient) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            string? Get(string key) => form.TryGetValue(key, out var v) ? v.ToString() : null;

            var grantType = Get("grant_type");

            if (grantType == "authorization_code")
            {
                var code = Get("code");
                var codeVerifier = Get("code_verifier");
                var redirectUri = Get("redirect_uri");

                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(codeVerifier))
                {
                    return Results.BadRequest(new OAuthErrorResponse("invalid_request", "Faltan code o code_verifier."));
                }

                var entry = store.TryConsumeCode(code);
                if (entry is null)
                {
                    return Results.BadRequest(new OAuthErrorResponse("invalid_grant", "Código inválido, caducado o ya usado."));
                }

                if (!string.IsNullOrEmpty(redirectUri) && redirectUri != entry.RedirectUri)
                {
                    return Results.BadRequest(new OAuthErrorResponse("invalid_grant", "redirect_uri no coincide."));
                }

                if (!PkceMatches(codeVerifier, entry.CodeChallenge))
                {
                    return Results.BadRequest(new OAuthErrorResponse("invalid_grant", "code_verifier no coincide con code_challenge."));
                }

                return Results.Json(BuildTokenResponse(entry.TriviUpJwt));
            }

            if (grantType == "refresh_token")
            {
                var refreshToken = Get("refresh_token");
                if (string.IsNullOrEmpty(refreshToken))
                {
                    return Results.BadRequest(new OAuthErrorResponse("invalid_request", "Falta refresh_token."));
                }

                try
                {
                    // El refresh_token que emitimos ES el JWT anterior: si todavía es válido,
                    // /Auth/refresh de TriviUp lo renueva. Si ya caducó, no hay nada que renovar
                    // y el cliente tendrá que volver a pasar por /authorize.
                    var renewed = await apiClient.RefreshAsync(refreshToken, ctx.RequestAborted);
                    return Results.Json(BuildTokenResponse(renewed.Token));
                }
                catch (TriviUpApiException)
                {
                    return Results.BadRequest(new OAuthErrorResponse("invalid_grant", "El token ya no se puede renovar; vuelve a autenticarte."));
                }
            }

            return Results.BadRequest(new OAuthErrorResponse("unsupported_grant_type"));
        });
    }

    private static IResult? ValidateAuthorizeRequest(IQueryCollection q, OAuthStore store, out ClientRegistration? client)
    {
        client = null;

        if (q["response_type"] != "code")
        {
            return Results.BadRequest(new OAuthErrorResponse("unsupported_response_type"));
        }

        var clientId = q["client_id"].ToString();
        var redirectUri = q["redirect_uri"].ToString();
        var codeChallenge = q["code_challenge"].ToString();
        var codeChallengeMethod = q["code_challenge_method"].ToString();

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(redirectUri) || string.IsNullOrEmpty(codeChallenge))
        {
            return Results.BadRequest(new OAuthErrorResponse("invalid_request", "Faltan client_id, redirect_uri o code_challenge."));
        }

        if (codeChallengeMethod != "S256")
        {
            return Results.BadRequest(new OAuthErrorResponse("invalid_request", "Solo se soporta code_challenge_method=S256."));
        }

        client = store.TryGetClient(clientId);
        if (client is null || !client.RedirectUris.Contains(redirectUri))
        {
            return Results.BadRequest(new OAuthErrorResponse("invalid_request", "client_id o redirect_uri desconocidos."));
        }

        return null;
    }

    private static bool PkceMatches(string codeVerifier, string codeChallenge)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var computed = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return computed == codeChallenge;
    }

    private static TokenResponse BuildTokenResponse(string jwt)
    {
        var expiresAt = JwtPeek.GetExpiry(jwt);
        var expiresIn = expiresAt.HasValue
            ? Math.Max(0, (long)(expiresAt.Value - DateTimeOffset.UtcNow).TotalSeconds)
            : 3600;

        return new TokenResponse(AccessToken: jwt, TokenType: "Bearer", ExpiresIn: expiresIn, RefreshToken: jwt);
    }

    private static string BaseUrl(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";

    private static string RenderLoginPage(string clientId, string redirectUri, string? state, string codeChallenge, string clientName, string? error)
    {
        var enc = HtmlEncoder.Default;
        var errorHtml = error is null ? "" : $"<p class=\"error\">{enc.Encode(error)}</p>";

        return $$"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Conectar con TriviUp</title>
                <style>
                    body { font-family: system-ui, sans-serif; background: #0f172a; color: #e2e8f0;
                           display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; }
                    form { background: #1e293b; padding: 2rem; border-radius: 12px; width: 320px; }
                    h1 { font-size: 1.1rem; margin: 0 0 0.25rem; }
                    p.subtitle { color: #94a3b8; font-size: 0.85rem; margin: 0 0 1.5rem; }
                    label { display: block; font-size: 0.85rem; margin-bottom: 0.25rem; }
                    input { width: 100%; box-sizing: border-box; padding: 0.6rem; margin-bottom: 1rem;
                            border-radius: 6px; border: 1px solid #334155; background: #0f172a; color: #e2e8f0; }
                    button { width: 100%; padding: 0.7rem; border-radius: 6px; border: none;
                             background: #6366f1; color: white; font-weight: 600; cursor: pointer; }
                    p.error { color: #f87171; font-size: 0.85rem; }
                </style>
            </head>
            <body>
                <form method="post" action="/authorize">
                    <h1>Conectar {{enc.Encode(clientName)}} con TriviUp</h1>
                    <p class="subtitle">Inicia sesión con tu cuenta de TriviUp para autorizar el acceso.</p>
                    {{errorHtml}}
                    <input type="hidden" name="client_id" value="{{enc.Encode(clientId)}}" />
                    <input type="hidden" name="redirect_uri" value="{{enc.Encode(redirectUri)}}" />
                    <input type="hidden" name="state" value="{{enc.Encode(state ?? "")}}" />
                    <input type="hidden" name="code_challenge" value="{{enc.Encode(codeChallenge)}}" />
                    <label for="username">Usuario o email</label>
                    <input id="username" name="username" autocomplete="username" required autofocus />
                    <label for="password">Contraseña</label>
                    <input id="password" name="password" type="password" autocomplete="current-password" required />
                    <button type="submit">Iniciar sesión</button>
                </form>
            </body>
            </html>
            """;
    }
}
