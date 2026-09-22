using System.Text.Json.Serialization;

namespace TriviUpMcp.Client;

// Estos records reflejan exactamente los contratos JSON expuestos por TriviUpBackend
// (ver TriviUpBackend/Cuestionarios/DTOs/BancoPreguntaDtos.cs y TriviUpBackend/DTO/User/*.cs).
// El MCP es un cliente HTTP externo: no referencia esos tipos directamente para no acoplar
// el ciclo de vida de ambos proyectos, solo reproduce su forma.

public record LoginRequest(string Username, string Password);

public record UserDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("profilePhotoUrl")] string? ProfilePhotoUrl
);

public record AuthResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("user")] UserDto User
);

public record BancoRespuesta(
    [property: JsonPropertyName("texto")] string Texto,
    [property: JsonPropertyName("esCorrecta")] bool EsCorrecta
);

public record BancoPreguntaRequest(
    [property: JsonPropertyName("enunciado")] string Enunciado,
    [property: JsonPropertyName("respuestas")] List<BancoRespuesta> Respuestas,
    [property: JsonPropertyName("imagenUrl")] string? ImagenUrl = null,
    [property: JsonPropertyName("dificultad")] string? Dificultad = null,
    [property: JsonPropertyName("categoriaId")] long? CategoriaId = null,
    [property: JsonPropertyName("categoriaNombre")] string? CategoriaNombre = null
);

public record BancoPreguntaResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("enunciado")] string Enunciado,
    [property: JsonPropertyName("imagenUrl")] string? ImagenUrl,
    [property: JsonPropertyName("respuestas")] List<BancoRespuesta> Respuestas,
    [property: JsonPropertyName("dificultad")] string? Dificultad,
    [property: JsonPropertyName("categoriaId")] long? CategoriaId,
    [property: JsonPropertyName("categoriaNombre")] string? CategoriaNombre,
    [property: JsonPropertyName("fechaCreacion")] DateTime FechaCreacion,
    [property: JsonPropertyName("fechaActualizacion")] DateTime FechaActualizacion
);

public record BancoPreguntaListResponse(
    [property: JsonPropertyName("preguntas")] List<BancoPreguntaResponse> Preguntas,
    [property: JsonPropertyName("totalCount")] int TotalCount
);

public record AsignarCategoriaRequest(
    [property: JsonPropertyName("preguntaIds")] List<long> PreguntaIds,
    [property: JsonPropertyName("categoriaId")] long? CategoriaId = null
);

public record AsignarCategoriaResponse(
    [property: JsonPropertyName("actualizadas")] int Actualizadas
);

public record BancoCategoriaRequest(
    [property: JsonPropertyName("nombre")] string Nombre
);

public record BancoCategoriaResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("total")] int Total
);

public record BancoCategoriasResponse(
    [property: JsonPropertyName("categorias")] List<BancoCategoriaResponse> Categorias,
    [property: JsonPropertyName("sinCategoria")] int SinCategoria
);

/// <summary>Forma típica de los errores que devuelve la API (400/401/403/404/500).</summary>
public record ApiErrorBody(
    [property: JsonPropertyName("message")] string? Message
);
