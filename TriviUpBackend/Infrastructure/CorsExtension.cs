namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Extensiones para aplicar la política CORS.
/// </summary>
public static class CorsExtension
{
    /// <summary>
    /// Aplica la política CORS configurada.
    /// </summary>
    public static IApplicationBuilder UseCorsPolicy(this IApplicationBuilder app)
    {
        return app.UseCors(CorsConfig.PolicyName);
    }
}