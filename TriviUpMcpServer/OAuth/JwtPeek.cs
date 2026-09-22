using System.Text.Json;

namespace TriviUpMcpServer.OAuth;

/// <summary>
/// Lee el claim "exp" de un JWT sin validar la firma: aquí solo nos interesa saber cuánto le
/// queda de vida para reportar expires_in. La validez real del token la decide siempre TriviUp
/// (lo comprobamos llamando a su API en TriviUpBearerAuthenticationHandler).
/// </summary>
public static class JwtPeek
{
    public static DateTimeOffset? GetExpiry(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return null;

        try
        {
            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("exp", out var expElement) && expElement.TryGetInt64(out var exp))
            {
                return DateTimeOffset.FromUnixTimeSeconds(exp);
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // JWT con forma inesperada: tratamos como "sin expiración conocida".
        }

        return null;
    }

    private static string Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
