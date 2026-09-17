namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Extensiones de middleware para el pipeline de la aplicación.
/// </summary>
public static class MiddlewareExtensions
{
    /// <summary>
    /// Middleware global de manejo de excepciones.
    /// Captura cualquier excepción no manejada y devuelve una respuesta JSON con el error.
    /// </summary>
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                var logger = context.RequestServices.GetService<ILogger<Program>>();
                logger.LogInformation("[MIDDLEWARE] Request started: {Method} {Path}", context.Request.Method, context.Request.Path);
                await next(); // Intentamos pasar la petición al siguiente paso del pipeline
                logger.LogInformation("[MIDDLEWARE] Request completed: {StatusCode}", context.Response.StatusCode);
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices.GetService<ILogger<Program>>();
                logger.LogError(ex, "[MIDDLEWARE] Unhandled exception: {Message}", ex.Message);
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { message = "Error inesperado: " + ex.Message });
            }
        });

        return app;
    }
}