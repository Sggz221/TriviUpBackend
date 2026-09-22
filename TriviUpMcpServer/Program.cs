using Microsoft.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore;
using TriviUpMcp.Auth;
using TriviUpMcp.Client;
using TriviUpMcp.Tools;
using TriviUpMcpServer;
using TriviUpMcpServer.OAuth;

var builder = WebApplication.CreateBuilder(args);

var apiBaseUrl = Environment.GetEnvironmentVariable("TRIVIUP_API_URL")
    ?? "https://triviup-backend-production.up.railway.app";

// Este proceso es compartido por todos los usuarios que se conecten al conector.
// El login de cada uno se guarda en SessionAuthStore (singleton) indexado por su
// Mcp-Session-Id (ver HttpSessionKeyProvider) -- NO en un DI "Scoped" normal, porque cada
// llamada HTTP dentro de una misma sesión MCP crea su propio scope de ASP.NET Core y por
// tanto NO comparte instancias Scoped con las llamadas anteriores de esa misma sesión.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SessionAuthStore>();
builder.Services.AddSingleton<OAuthStore>();
builder.Services.AddScoped<ISessionKeyProvider, HttpSessionKeyProvider>();
builder.Services.AddScoped<AuthSessionState>();
builder.Services.AddHttpClient<TriviUpApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
});

// Conector protegido por OAuth (ver OAuth/): claude.ai exige que un "custom connector" hable
// OAuth para registrarse y autenticar, así que /mcp requiere un Bearer token válido -- que ES
// el JWT de TriviUp obtenido durante /authorize. Con esto, el usuario ya llega autenticado a
// la conversación y no necesita llamar a la tool 'login' (aunque sigue disponible por si hace
// falta cambiar de cuenta a mitad de sesión).
builder.Services
    .AddAuthentication(TriviUpBearerAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, TriviUpBearerAuthenticationHandler>(
        TriviUpBearerAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "triviup-banco-preguntas", Version = "1.0.0" };
        options.ServerInstructions =
            "El usuario ya está autenticado como su cuenta de TriviUp (OAuth del conector). " +
            "Usa 'whoami' si necesitas confirmar quién es. Solo llama a 'login' si el usuario pide " +
            "explícitamente cambiar de cuenta dentro de la misma conversación. " +
            "Cada pregunta debe tener al menos 2 respuestas y exactamente una marcada como correcta.";
    })
    .WithHttpTransport(options =>
    {
        // Mantiene la sesión (y el token, sembrado por TriviUpBearerAuthenticationHandler en
        // cada request autenticado) viva entre llamadas dentro de la misma sesión MCP.
        options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
    })
    .WithToolsFromAssembly(typeof(AuthTools).Assembly);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapTriviUpOAuthEndpoints();
app.MapMcp("/mcp").RequireAuthorization();
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();
