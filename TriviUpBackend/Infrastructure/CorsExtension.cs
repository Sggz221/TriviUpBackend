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
        var env = ((WebApplication)app).Environment;

        var policyName = "AllowAll"; // Cambiar luego según queramos

        return app.UseCors(policyName);
    }
}