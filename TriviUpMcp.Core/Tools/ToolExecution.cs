using System.Text.Json;
using TriviUpMcp.Client;

namespace TriviUpMcp.Tools;

/// <summary>
/// Ejecuta la llamada a la API y serializa el resultado (o el error) a JSON legible.
/// Los errores nunca se propagan como excepción de protocolo: se devuelven como texto
/// para que el agente de IA pueda leerlos y corregir su siguiente llamada.
/// </summary>
internal static class ToolExecution
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            var result = await action();
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (TriviUpApiException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, statusCode = ex.StatusCode }, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Error inesperado: {ex.Message}" }, JsonOptions);
        }
    }
}
