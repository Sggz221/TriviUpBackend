using System.Text.Json;

namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Configuración de los controladores de la API.
/// </summary>
public static class ControllerConfig
{
    public static IServiceCollection AddControllersConfiguration(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
        
        services.AddControllers(options =>
        {
            options.SuppressAsyncSuffixInActionNames = false;
        });

        return services;
    }
}