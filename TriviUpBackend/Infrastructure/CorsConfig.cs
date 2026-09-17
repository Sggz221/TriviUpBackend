namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Configuración de CORS (Cross-Origin Resource Sharing).
/// Define las políticas de acceso desde diferentes orígenes.
/// </summary>
public static class CorsConfig
{
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {

        return services.AddCors(options =>
        {
            if (isDevelopment)
            {
                // SignalR requiere credenciales, así que no podemos usar AllowAnyOrigin
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.WithOrigins("http://localhost:4200", "http://localhost:5164")
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials();
                });
            }
            else
            {
                // Leer directamente de environment variable
                var allowedOriginsString = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")
                    ?? throw new InvalidOperationException("ALLOWED_ORIGINS no configurado");

                var allowedOrigins = allowedOriginsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray();

                if (allowedOrigins.Length == 0)
                    throw new InvalidOperationException("Cors:AllowedOrigins no puede estar vacío");

                options.AddPolicy("ProductionPolicy", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials();
                });
            }
        });
    }
}