using Microsoft.AspNetCore.Mvc;

namespace TriviUpBackend.Infrastructure;

/// <summary>
/// Configuración de validación de modelos para la API.
/// Personaliza las respuestas de error de validación.
/// </summary>
public static class ValidationConfig
{
    public static IServiceCollection AddCustomValidation(this IServiceCollection services)
    {
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                // Extraemos todos los mensajes de error y los unimos en una sola cadena de texto
                var mensaje = string.Join(", ", context.ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

                // Devolvemos propio objeto BadRequest con el formato JSON que hemos definido
                return new BadRequestObjectResult(new { message = mensaje });
            };
        });

        return services;
    }
}