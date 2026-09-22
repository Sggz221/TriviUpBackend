using System.Net.Http.Headers;
using System.Net.Http.Json;
using TriviUpMcp.Auth;

namespace TriviUpMcp.Client;

/// <summary>
/// Cliente HTTP tipado para la API REST de TriviUp (Auth, Users, banco de preguntas/categorías).
/// Añade automáticamente el Bearer token de <see cref="AuthSessionState"/> a cada petición
/// que lo necesite. El MCP se comporta como cualquier cliente externo autenticado.
/// </summary>
public class TriviUpApiClient(HttpClient httpClient, AuthSessionState session)
{
    public async Task<AuthResponse> SignInAsync(string username, string password, CancellationToken ct)
    {
        var response = await httpClient.PostAsJsonAsync("Auth/signin", new LoginRequest(username, password), ct);
        return await ReadOrThrowAsync<AuthResponse>(response, ct);
    }

    public async Task<UserDto> GetMeAsync(CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "Users/me", null, ct);
        return await ReadOrThrowAsync<UserDto>(response, ct);
    }

    public async Task<BancoCategoriasResponse> ListCategoriasAsync(CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, "api/banco-categorias", null, ct);
        return await ReadOrThrowAsync<BancoCategoriasResponse>(response, ct);
    }

    public async Task<BancoCategoriaResponse> CreateCategoriaAsync(string nombre, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Post, "api/banco-categorias", new BancoCategoriaRequest(nombre), ct);
        return await ReadOrThrowAsync<BancoCategoriaResponse>(response, ct);
    }

    public async Task<BancoCategoriaResponse> RenameCategoriaAsync(long id, string nombre, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Put, $"api/banco-categorias/{id}", new BancoCategoriaRequest(nombre), ct);
        return await ReadOrThrowAsync<BancoCategoriaResponse>(response, ct);
    }

    public async Task DeleteCategoriaAsync(long id, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Delete, $"api/banco-categorias/{id}", null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<BancoPreguntaListResponse> ListPreguntasAsync(
        string? q, long? categoriaId, bool sinCategoria, string? dificultad, int page, int pageSize, CancellationToken ct)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (categoriaId.HasValue) query.Add($"categoriaId={categoriaId.Value}");
        if (sinCategoria) query.Add("sinCategoria=true");
        if (!string.IsNullOrWhiteSpace(dificultad)) query.Add($"dificultad={Uri.EscapeDataString(dificultad)}");
        query.Add($"page={page}");
        query.Add($"pageSize={pageSize}");

        var response = await SendAuthorizedAsync(HttpMethod.Get, $"api/banco-preguntas?{string.Join('&', query)}", null, ct);
        return await ReadOrThrowAsync<BancoPreguntaListResponse>(response, ct);
    }

    public async Task<BancoPreguntaResponse> GetPreguntaAsync(long id, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Get, $"api/banco-preguntas/{id}", null, ct);
        return await ReadOrThrowAsync<BancoPreguntaResponse>(response, ct);
    }

    public async Task<BancoPreguntaResponse> CreatePreguntaAsync(BancoPreguntaRequest request, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Post, "api/banco-preguntas", request, ct);
        return await ReadOrThrowAsync<BancoPreguntaResponse>(response, ct);
    }

    public async Task<BancoPreguntaResponse> UpdatePreguntaAsync(long id, BancoPreguntaRequest request, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Put, $"api/banco-preguntas/{id}", request, ct);
        return await ReadOrThrowAsync<BancoPreguntaResponse>(response, ct);
    }

    public async Task DeletePreguntaAsync(long id, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(HttpMethod.Delete, $"api/banco-preguntas/{id}", null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<AsignarCategoriaResponse> AsignarCategoriaAsync(List<long> preguntaIds, long? categoriaId, CancellationToken ct)
    {
        var response = await SendAuthorizedAsync(
            HttpMethod.Post, "api/banco-preguntas/asignar-categoria", new AsignarCategoriaRequest(preguntaIds, categoriaId), ct);
        return await ReadOrThrowAsync<AsignarCategoriaResponse>(response, ct);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var token = session.GetTokenOrNull()
            ?? throw new TriviUpApiException(401, "No autenticado: usa la tool 'login' antes de llamar a esta tool.");

        using var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await httpClient.SendAsync(request, ct);
    }

    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        var result = await response.Content.ReadFromJsonAsync<T>(ct);
        return result ?? throw new TriviUpApiException((int)response.StatusCode, "La API devolvió una respuesta vacía inesperada.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string message = $"Error HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ApiErrorBody>(ct);
            if (!string.IsNullOrWhiteSpace(body?.Message))
            {
                message = body!.Message!;
            }
        }
        catch
        {
            // El cuerpo no era JSON con "message"; nos quedamos con el mensaje genérico.
        }

        throw new TriviUpApiException((int)response.StatusCode, message);
    }
}
