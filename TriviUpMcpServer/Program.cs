using ModelContextProtocol.AspNetCore;
using TriviUpMcp.Auth;
using TriviUpMcp.Client;
using TriviUpMcp.Tools;
using TriviUpMcpServer;

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
builder.Services.AddScoped<ISessionKeyProvider, HttpSessionKeyProvider>();
builder.Services.AddScoped<AuthSessionState>();
builder.Services.AddHttpClient<TriviUpApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "triviup-banco-preguntas", Version = "1.0.0" };
        options.ServerInstructions =
            "Antes de usar cualquier otra tool, llama a 'login' con las credenciales de tu cuenta de TriviUp. " +
            "El token queda guardado en tu sesión MCP (no se comparte con otros usuarios conectados a este mismo servidor). " +
            "Cada pregunta debe tener al menos 2 respuestas y exactamente una marcada como correcta.";
    })
    .WithHttpTransport(options =>
    {
        // Mantiene el token de 'login' vivo entre llamadas dentro de la misma sesión MCP
        // (clientes que hacen el handshake 'initialize', que es lo que usan hoy Claude
        // Desktop/Code/claude.ai). Clientes futuros sin sesión (protocolo 2026-07-28+)
        // caerían a un contexto nuevo por request y tendrían que hacer login en cada llamada.
        options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
    })
    .WithToolsFromAssembly(typeof(AuthTools).Assembly);

var app = builder.Build();

app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();
