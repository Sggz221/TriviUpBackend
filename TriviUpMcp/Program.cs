using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TriviUpMcp.Auth;
using TriviUpMcp.Client;

var builder = Host.CreateApplicationBuilder(args);

// stdio es el transporte del MCP: stdout solo puede llevar mensajes del protocolo,
// así que todo el logging debe ir a stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var apiBaseUrl = Environment.GetEnvironmentVariable("TRIVIUP_API_URL") ?? "http://localhost:5164";

builder.Services.AddSingleton<AuthSessionState>();
builder.Services.AddHttpClient<TriviUpApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "triviup-banco-preguntas", Version = "1.0.0" };
        options.ServerInstructions =
            "Antes de usar cualquier otra tool, llama a 'login' con las credenciales del usuario de TriviUp. " +
            "El token queda guardado en memoria para el resto de la sesión. " +
            "Cada pregunta debe tener al menos 2 respuestas y exactamente una marcada como correcta.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
