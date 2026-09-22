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

// ---------- Cuestionarios (api/cuestionarios, api/quizzes) ----------
// Ver TriviUpBackend/Cuestionarios/DTOs/CreateQuizRequest.cs, UpdateQuizRequest.cs, QuizResponse.cs, PublicQuizResponse.cs.

public record RespuestaCuestionarioInput(
    [property: JsonPropertyName("texto")] string Texto,
    [property: JsonPropertyName("esCorrecta")] bool EsCorrecta = false
);

/// <summary>
/// Pregunta de un cuestionario en creación o edición. Tanto crear como editar reemplazan las
/// preguntas por completo (la API las recrea con ids nuevos cada vez), por eso no lleva Id.
/// </summary>
public record PreguntaCuestionarioInput(
    [property: JsonPropertyName("numeroPregunta")] int NumeroPregunta,
    [property: JsonPropertyName("enunciado")] string Enunciado,
    [property: JsonPropertyName("respuestas")] List<RespuestaCuestionarioInput> Respuestas,
    [property: JsonPropertyName("imagenUrl")] string? ImagenUrl = null,
    [property: JsonPropertyName("dificultad")] string? Dificultad = null,
    [property: JsonPropertyName("faseNumero")] int FaseNumero = 1,
    [property: JsonPropertyName("faseNombre")] string? FaseNombre = null,
    [property: JsonPropertyName("faseColor")] string? FaseColor = null
);

public record CreateCuestionarioRequest(
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("preguntas")] List<PreguntaCuestionarioInput> Preguntas,
    [property: JsonPropertyName("esPublico")] bool EsPublico = false,
    [property: JsonPropertyName("esBorrador")] bool EsBorrador = false
);

/// <summary><see cref="EsPublico"/> null = no cambiarlo.</summary>
public record UpdateCuestionarioRequest(
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("preguntas")] List<PreguntaCuestionarioInput> Preguntas,
    [property: JsonPropertyName("esPublico")] bool? EsPublico = null,
    [property: JsonPropertyName("esBorrador")] bool EsBorrador = false
);

public record RespuestaCuestionarioResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("texto")] string Texto,
    [property: JsonPropertyName("esCorrecta")] bool EsCorrecta
);

public record PreguntaCuestionarioResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("numeroPregunta")] int NumeroPregunta,
    [property: JsonPropertyName("enunciado")] string Enunciado,
    [property: JsonPropertyName("imagenUrl")] string? ImagenUrl,
    [property: JsonPropertyName("faseNumero")] int FaseNumero,
    [property: JsonPropertyName("faseNombre")] string? FaseNombre,
    [property: JsonPropertyName("faseColor")] string? FaseColor,
    [property: JsonPropertyName("dificultad")] string? Dificultad,
    [property: JsonPropertyName("respuestas")] List<RespuestaCuestionarioResponse> Respuestas
);

public record CuestionarioResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("gameCode")] string GameCode,
    [property: JsonPropertyName("esPublico")] bool EsPublico,
    [property: JsonPropertyName("esBorrador")] bool EsBorrador,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("tieneBorrador")] bool TieneBorrador,
    [property: JsonPropertyName("preguntas")] List<PreguntaCuestionarioResponse> Preguntas,
    [property: JsonPropertyName("creatorId")] long CreatorId,
    [property: JsonPropertyName("fechaCreacion")] DateTime FechaCreacion,
    [property: JsonPropertyName("fechaActualizacion")] DateTime FechaActualizacion
);

public record CuestionarioListPageResponse(
    [property: JsonPropertyName("quizzes")] List<CuestionarioResponse> Quizzes,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize
);
