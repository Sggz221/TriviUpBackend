namespace TriviUpMcp.Client;

/// <summary>Error devuelto por la API de TriviUp (HTTP no-2xx), con el mensaje ya extraído del cuerpo JSON.</summary>
public class TriviUpApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
