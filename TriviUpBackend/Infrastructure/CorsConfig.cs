namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Configuración de CORS (Cross-Origin Resource Sharing).
/// Define las políticas de acceso desde diferentes orígenes.
/// </summary>
public static class CorsConfig
{
    /// <summary>
    /// Nombre único de la política CORS, usado tanto al registrarla como al aplicarla
    /// (antes había un mismatch: se registraba "AllowAll"/"ProductionPolicy" según el
    /// entorno, pero siempre se aplicaba "AllowAll" -> en producción no se encontraba
    /// la política y CORS quedaba silenciosamente desactivado).
    /// </summary>
    public const string PolicyName = "AppCors";

    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {

        return services.AddCors(options =>
        {
            if (isDevelopment)
            {
                // SignalR requiere credenciales, así que no podemos usar AllowAnyOrigin
                options.AddPolicy(PolicyName, policy =>
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

                options.AddPolicy(PolicyName, policy =>
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