using TriviUpBackend.Infrastructure;
using TriviUpBackend.Game.Hubs;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Registro de Servicios (Inyección de Dependencias) ---

// Configuración de Controladores
builder.Services.AddControllersConfiguration();

// OpenAPI / Swagger
builder.Services.AddOpenApi();

// Base de Datos (InMemory según tu DatabaseConfig)
builder.Services.AddDatabase(builder.Configuration);

// Repositorios y Servicios de Aplicación
builder.Services.AddRepositoriesAndServices(builder.Configuration);

// Autenticación y Autorización (JWT)
builder.Services.AddAuthentication(builder.Configuration);

// Configuración de CORS
builder.Services.AddCorsPolicy(builder.Configuration, builder.Environment.IsDevelopment());

// Validaciones personalizadas para BadRequest
builder.Services.AddCustomValidation();

// Storage
builder.Services.AddStorage();

// Game runtime: SignalR (+ Redis backplane), session store, deadline worker
builder.Services.AddGameRuntime(builder.Configuration, builder.Environment);

var app = builder.Build();

// --- 2. Configuración del Pipeline de HTTP (Middleware) ---

// Exception Handler Global (Tu extensión)
app.UseGlobalExceptionHandler();

app.MapOpenApi();

// HTTPS Redirection deshabilitado - Railway maneja HTTPS en el proxy
// app.UseHttpsRedirection();

// CORS: Debe ir después de Routing y antes de Authentication/Authorization
app.UseRouting();

// Extensión de CORS
app.UseCorsPolicy(); 

app.UseAuthentication();
app.UseAuthorization();

app.SeedDatabase();

// Mapeo de Controladores
app.MapControllers();

// Health checks (Railway / load balancers)
app.MapHealthChecks("/health");

// Game Hub (SignalR)
app.MapHub<GameHub>("/hubs/game");

app.Run();
