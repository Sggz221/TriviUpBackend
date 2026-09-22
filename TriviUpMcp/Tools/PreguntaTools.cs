using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TriviUpMcp.Client;

namespace TriviUpMcp.Tools;

/// <summary>Respuesta tal como la manda el agente al crear/editar una pregunta.</summary>
public record RespuestaInput(
    [property: JsonPropertyName("texto")] string Texto,
    [property: JsonPropertyName("esCorrecta")] bool EsCorrecta = false
);

[McpServerToolType]
public class PreguntaTools(TriviUpApiClient client)
{
    [McpServerTool(Name = "listar_preguntas", ReadOnly = true)]
    [Description("Lista las preguntas del banco personal del usuario autenticado, con filtros opcionales de búsqueda, categoría y dificultad.")]
    public Task<string> ListarPreguntas(
        [Description("Texto a buscar en el enunciado (opcional)")] string? q = null,
        [Description("Filtra por id de categoría (opcional)")] long? categoriaId = null,
        [Description("Si es true, solo devuelve preguntas sin categoría")] bool sinCategoria = false,
        [Description("Filtra por dificultad: facil, media o dificil (opcional)")] string? dificultad = null,
        [Description("Número de página (por defecto 1)")] int page = 1,
        [Description("Tamaño de página, máximo 100 (por defecto 20)")] int pageSize = 20,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(() => client.ListPreguntasAsync(
            q, categoriaId, sinCategoria, dificultad, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize, ct));
    }

    [McpServerTool(Name = "obtener_pregunta", ReadOnly = true)]
    [Description("Obtiene el detalle completo de una pregunta del banco por su id (enunciado, respuestas, dificultad, categoría).")]
    public Task<string> ObtenerPregunta(
        [Description("Id de la pregunta")] long id,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(() => client.GetPreguntaAsync(id, ct));
    }

    [McpServerTool(Name = "crear_pregunta")]
    [Description("Crea una nueva pregunta en el banco personal del usuario autenticado. " +
                 "Debe tener al menos 2 respuestas y exactamente una marcada como correcta. " +
                 "La categoría se indica por id (categoriaId, si ya existe) o por nombre (categoriaNombre, se crea si no existe).")]
    public Task<string> CrearPregunta(
        [Description("Enunciado de la pregunta (máx. 1000 caracteres)")] string enunciado,
        [Description("Lista de respuestas: cada una con 'texto' y 'esCorrecta'. Mínimo 2, y exactamente una debe ser correcta.")] List<RespuestaInput> respuestas,
        [Description("Dificultad: facil, media o dificil (opcional, sin clasificar si se omite)")] string? dificultad = null,
        [Description("Id de una categoría ya existente del usuario (opcional, tiene prioridad sobre categoriaNombre)")] long? categoriaId = null,
        [Description("Nombre de categoría: se reutiliza si ya existe o se crea nueva (opcional)")] string? categoriaNombre = null,
        [Description("URL de una imagen asociada a la pregunta (opcional)")] string? imagenUrl = null,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(async () =>
        {
            ValidateRespuestas(respuestas);
            var request = new BancoPreguntaRequest(
                enunciado, ToBancoRespuestas(respuestas), imagenUrl, dificultad, categoriaId, categoriaNombre);
            return await client.CreatePreguntaAsync(request, ct);
        });
    }

    [McpServerTool(Name = "editar_pregunta")]
    [Description("Reemplaza por completo una pregunta existente (enunciado, respuestas, dificultad y categoría). " +
                 "Para cambiar un solo campo sin tocar el resto, usa mejor editar_dificultad_pregunta, anadir_respuesta o marcar_respuesta_correcta.")]
    public Task<string> EditarPregunta(
        [Description("Id de la pregunta a editar")] long id,
        [Description("Nuevo enunciado (máx. 1000 caracteres)")] string enunciado,
        [Description("Lista completa de respuestas resultante: mínimo 2, y exactamente una correcta.")] List<RespuestaInput> respuestas,
        [Description("Dificultad: facil, media o dificil (opcional)")] string? dificultad = null,
        [Description("Id de una categoría ya existente del usuario (opcional)")] long? categoriaId = null,
        [Description("Nombre de categoría: se reutiliza si ya existe o se crea nueva (opcional)")] string? categoriaNombre = null,
        [Description("URL de una imagen asociada a la pregunta (opcional)")] string? imagenUrl = null,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(async () =>
        {
            ValidateRespuestas(respuestas);
            var request = new BancoPreguntaRequest(
                enunciado, ToBancoRespuestas(respuestas), imagenUrl, dificultad, categoriaId, categoriaNombre);
            return await client.UpdatePreguntaAsync(id, request, ct);
        });
    }

    [McpServerTool(Name = "eliminar_pregunta", Destructive = true)]
    [Description("Borra definitivamente una pregunta del banco personal.")]
    public Task<string> EliminarPregunta(
        [Description("Id de la pregunta a eliminar")] long id,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(async () =>
        {
            await client.DeletePreguntaAsync(id, ct);
            return new { message = $"Pregunta {id} eliminada." };
        });
    }

    [McpServerTool(Name = "asignar_categoria_preguntas")]
    [Description("Mueve varias preguntas a una categoría de una sola vez, o las deja sin categoría si no se indica categoriaId.")]
    public Task<string> AsignarCategoriaPreguntas(
        [Description("Ids de las preguntas a mover")] List<long> preguntaIds,
        [Description("Id de la categoría destino, o null para dejarlas sin categoría")] long? categoriaId = null,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(() => client.AsignarCategoriaAsync(preguntaIds, categoriaId, ct));
    }

    [McpServerTool(Name = "editar_dificultad_pregunta")]
    [Description("Cambia solo la dificultad de una pregunta existente, sin tocar enunciado, respuestas ni categoría.")]
    public Task<string> EditarDificultadPregunta(
        [Description("Id de la pregunta")] long id,
        [Description("Nueva dificultad: facil, media o dificil (usa null/vacío para dejarla sin clasificar)")] string? dificultad = null,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(async () =>
        {
            var actual = await client.GetPreguntaAsync(id, ct);
            var request = RequestFromCurrent(actual) with { Dificultad = dificultad };
            return await client.UpdatePreguntaAsync(id, request, ct);
        });
    }

    [McpServerTool(Name = "anadir_respuesta")]
    [Description("Añade una nueva respuesta a una pregunta existente, conservando las respuestas que ya tenía.")]
    public Task<string> AnadirRespuesta(
        [Description("Id de la pregunta")] long id,
        [Description("Texto de la nueva respuesta")] string texto,
        [Description("Si esta nueva respuesta es la correcta (por defecto false; si es true, desmarca las demás)")] bool esCorrecta = false,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(async () =>
        {
            var actual = await client.GetPreguntaAsync(id, ct);
            var respuestas = actual.Respuestas
                .Select(r => esCorrecta ? r with { EsCorrecta = false } : r)
                .Append(new BancoRespuesta(texto, esCorrecta))
                .ToList();

            var request = RequestFromCurrent(actual) with { Respuestas = respuestas };
            return await client.UpdatePreguntaAsync(id, request, ct);
        });
    }

    [McpServerTool(Name = "marcar_respuesta_correcta")]
    [Description("Marca una respuesta concreta de una pregunta como la correcta y desmarca el resto " +
                 "(una pregunta debe tener siempre exactamente una respuesta correcta). " +
                 "Identifica la respuesta por su posición (0-based) o por su texto exacto; indica solo uno de los dos.")]
    public Task<string> MarcarRespuestaCorrecta(
        [Description("Id de la pregunta")] long id,
        [Description("Posición (0-based) de la respuesta a marcar como correcta, según el orden devuelto por obtener_pregunta (opcional si se usa respuestaTexto)")] int? respuestaIndex = null,
        [Description("Texto exacto de la respuesta a marcar como correcta (opcional si se usa respuestaIndex)")] string? respuestaTexto = null,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(async () =>
        {
            var actual = await client.GetPreguntaAsync(id, ct);

            int targetIndex = ResolveRespuestaIndex(actual.Respuestas, respuestaIndex, respuestaTexto);

            var respuestas = actual.Respuestas
                .Select((r, i) => r with { EsCorrecta = i == targetIndex })
                .ToList();

            var request = RequestFromCurrent(actual) with { Respuestas = respuestas };
            return await client.UpdatePreguntaAsync(id, request, ct);
        });
    }

    private static int ResolveRespuestaIndex(List<BancoRespuesta> respuestas, int? respuestaIndex, string? respuestaTexto)
    {
        if (respuestaIndex is null && string.IsNullOrWhiteSpace(respuestaTexto))
        {
            throw new TriviUpApiException(400, "Indica respuestaIndex o respuestaTexto para identificar la respuesta correcta.");
        }

        if (respuestaIndex is not null)
        {
            if (respuestaIndex.Value < 0 || respuestaIndex.Value >= respuestas.Count)
            {
                throw new TriviUpApiException(400,
                    $"respuestaIndex {respuestaIndex.Value} fuera de rango: la pregunta tiene {respuestas.Count} respuestas (0 a {respuestas.Count - 1}).");
            }

            return respuestaIndex.Value;
        }

        var index = respuestas.FindIndex(r => r.Texto == respuestaTexto);
        if (index < 0)
        {
            throw new TriviUpApiException(400, $"No se encontró ninguna respuesta con el texto exacto '{respuestaTexto}'.");
        }

        return index;
    }

    private static void ValidateRespuestas(List<RespuestaInput> respuestas)
    {
        if (respuestas.Count < 2 || respuestas.Any(r => string.IsNullOrWhiteSpace(r.Texto)))
        {
            throw new TriviUpApiException(400, "La pregunta debe tener al menos 2 respuestas con texto.");
        }

        if (respuestas.Count(r => r.EsCorrecta) != 1)
        {
            throw new TriviUpApiException(400, "La pregunta debe tener exactamente una respuesta marcada como correcta.");
        }
    }

    private static List<BancoRespuesta> ToBancoRespuestas(List<RespuestaInput> respuestas) =>
        respuestas.Select(r => new BancoRespuesta(r.Texto, r.EsCorrecta)).ToList();

    /// <summary>Reconstruye el request de escritura a partir del estado actual, para operaciones read-modify-write.</summary>
    private static BancoPreguntaRequest RequestFromCurrent(BancoPreguntaResponse actual) => new(
        actual.Enunciado, actual.Respuestas, actual.ImagenUrl, actual.Dificultad, actual.CategoriaId, actual.CategoriaNombre);
}
