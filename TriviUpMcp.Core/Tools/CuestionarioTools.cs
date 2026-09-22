using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using TriviUpMcp.Client;

namespace TriviUpMcp.Tools;

[McpServerToolType]
public partial class CuestionarioTools(TriviUpApiClient client)
{
    [McpServerTool(Name = "crear_cuestionario")]
    [Description("Crea un nuevo cuestionario (quiz) con sus preguntas y respuestas. " +
                 "Cada pregunta necesita al menos 2 respuestas con texto y exactamente una correcta. " +
                 "Las preguntas se agrupan en fases (faseNumero, empezando en 1): deben ser consecutivas, " +
                 "sin saltos, y todas las preguntas de una misma fase deben compartir faseNombre y faseColor.")]
    public Task<string> CrearCuestionario(
        [Description("Nombre del cuestionario (máx. 100 caracteres)")] string nombre,
        [Description("Preguntas del cuestionario. Cada una: numeroPregunta, enunciado, respuestas [{texto, esCorrecta}], y opcionalmente imagenUrl, dificultad, faseNumero, faseNombre, faseColor.")] List<PreguntaCuestionarioInput> preguntas,
        [Description("Si el cuestionario es visible públicamente (por defecto false)")] bool esPublico = false,
        [Description("Si se guarda como borrador: se relajan las validaciones (por defecto false)")] bool esBorrador = false,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(async () =>
        {
            if (!esBorrador) ValidatePreguntas(preguntas);
            var request = new CreateCuestionarioRequest(nombre, preguntas, esPublico, esBorrador);
            return await client.CreateCuestionarioAsync(request, ct);
        });
    }

    [McpServerTool(Name = "listar_cuestionarios", ReadOnly = true)]
    [Description("Lista todos los cuestionarios del sistema de forma paginada (no filtra por creador).")]
    public Task<string> ListarCuestionarios(
        [Description("Número de página (por defecto 1)")] int page = 1,
        [Description("Tamaño de página (por defecto 10)")] int pageSize = 10,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(() => client.ListCuestionariosAsync(page <= 0 ? 1 : page, pageSize <= 0 ? 10 : pageSize, ct));
    }

    [McpServerTool(Name = "listar_mis_cuestionarios", ReadOnly = true)]
    [Description("Lista los cuestionarios creados por el usuario autenticado.")]
    public Task<string> ListarMisCuestionarios(CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(() => client.ListMisCuestionariosAsync(ct));
    }

    [McpServerTool(Name = "obtener_cuestionario", ReadOnly = true)]
    [Description("Obtiene el detalle completo de un cuestionario por su id (nombre, gameCode, preguntas con respuestas). " +
                 "Es público: no hace falta login salvo para ver un borrador propio.")]
    public Task<string> ObtenerCuestionario(
        [Description("Id del cuestionario")] long id,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(() => client.GetCuestionarioAsync(id, ct));
    }

    [McpServerTool(Name = "obtener_cuestionario_por_codigo", ReadOnly = true)]
    [Description("Obtiene un cuestionario publicado por su gameCode (código de 6 caracteres para unirse a la partida). Es público.")]
    public Task<string> ObtenerCuestionarioPorCodigo(
        [Description("Código de juego del cuestionario (ej. \"AB12CD\")")] string gameCode,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(() => client.GetCuestionarioPorCodigoAsync(gameCode, ct));
    }

    [McpServerTool(Name = "editar_cuestionario")]
    [Description("Reemplaza por completo un cuestionario existente (nombre, preguntas y respuestas). " +
                 "Mismas reglas que crear_cuestionario. Si el cuestionario ya estaba publicado, esto crea una nueva versión " +
                 "(la anterior queda archivada, consultable con las tools de versiones si se añaden más adelante).")]
    public Task<string> EditarCuestionario(
        [Description("Id del cuestionario a editar")] long id,
        [Description("Nuevo nombre del cuestionario")] string nombre,
        [Description("Lista completa de preguntas resultante: sustituye TODAS las preguntas y respuestas existentes por estas (la API las recrea con ids nuevos en cada edición, así que no hace falta ni sirve reenviar los ids antiguos).")] List<PreguntaCuestionarioInput> preguntas,
        [Description("Si el cuestionario es público (null = no cambiar el valor actual)")] bool? esPublico = null,
        [Description("Si se guarda como borrador: se relajan las validaciones (por defecto false)")] bool esBorrador = false,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(async () =>
        {
            if (!esBorrador) ValidatePreguntas(preguntas);
            var request = new UpdateCuestionarioRequest(nombre, preguntas, esPublico, esBorrador);
            return await client.UpdateCuestionarioAsync(id, request, ct);
        });
    }

    [McpServerTool(Name = "eliminar_cuestionario", Destructive = true)]
    [Description("Borra definitivamente un cuestionario. Solo el creador puede eliminarlo.")]
    public Task<string> EliminarCuestionario(
        [Description("Id del cuestionario a eliminar")] long id,
        CancellationToken ct = default)
    {
        return ToolExecution.RunAsync(async () =>
        {
            await client.DeleteCuestionarioAsync(id, ct);
            return new { message = $"Cuestionario {id} eliminado." };
        });
    }

    private static readonly string[] DificultadesValidas = ["facil", "media", "dificil"];

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex FaseColorRegex();

    /// <summary>Replica las validaciones de QuizService (respuestas, dificultad, color y secuencia de fases) para dar errores claros antes de llamar a la API.</summary>
    private static void ValidatePreguntas(List<PreguntaCuestionarioInput> preguntas)
    {
        if (preguntas.Count == 0)
        {
            throw new TriviUpApiException(400, "Debe haber al menos una pregunta.");
        }

        for (int i = 0; i < preguntas.Count; i++)
        {
            var p = preguntas[i];

            if (string.IsNullOrWhiteSpace(p.Enunciado))
            {
                throw new TriviUpApiException(400, $"La pregunta {i + 1} no puede estar vacía.");
            }

            if (p.Respuestas.Count < 2 || p.Respuestas.Any(r => string.IsNullOrWhiteSpace(r.Texto)))
            {
                throw new TriviUpApiException(400, $"La pregunta {i + 1} debe tener al menos 2 respuestas con texto.");
            }

            if (p.Respuestas.Count(r => r.EsCorrecta) != 1)
            {
                throw new TriviUpApiException(400, $"La pregunta {i + 1} debe tener exactamente una respuesta correcta.");
            }

            if (!string.IsNullOrWhiteSpace(p.Dificultad) && !DificultadesValidas.Contains(p.Dificultad.Trim().ToLowerInvariant()))
            {
                throw new TriviUpApiException(400, $"Dificultad no válida en la pregunta {i + 1}: \"{p.Dificultad}\". Usa facil, media o dificil.");
            }

            if (!string.IsNullOrWhiteSpace(p.FaseColor) && !FaseColorRegex().IsMatch(p.FaseColor.Trim()))
            {
                throw new TriviUpApiException(400, $"Color de fase no válido en la pregunta {i + 1}: \"{p.FaseColor}\". Usa el formato #rrggbb.");
            }
        }

        ValidateFases(preguntas);
    }

    private static void ValidateFases(List<PreguntaCuestionarioInput> preguntas)
    {
        var fase = 1;
        string? nombreFase = null;
        string? colorFase = null;
        var primera = true;

        foreach (var p in preguntas.OrderBy(p => p.NumeroPregunta))
        {
            var nombre = string.IsNullOrWhiteSpace(p.FaseNombre) ? null : p.FaseNombre.Trim();
            var color = string.IsNullOrWhiteSpace(p.FaseColor) ? null : p.FaseColor.Trim().ToLowerInvariant();

            if (primera)
            {
                if (p.FaseNumero != 1)
                {
                    throw new TriviUpApiException(400, "Las fases deben empezar en 1.");
                }
                nombreFase = nombre;
                colorFase = color;
                primera = false;
                continue;
            }

            if (p.FaseNumero == fase)
            {
                if (nombre != nombreFase)
                {
                    throw new TriviUpApiException(400, $"Las preguntas de la fase {fase} deben tener el mismo nombre de fase.");
                }
                if (color != colorFase)
                {
                    throw new TriviUpApiException(400, $"Las preguntas de la fase {fase} deben tener el mismo color de fase.");
                }
            }
            else if (p.FaseNumero == fase + 1)
            {
                fase = p.FaseNumero;
                nombreFase = nombre;
                colorFase = color;
            }
            else
            {
                throw new TriviUpApiException(400, "Las fases deben ser consecutivas y no puede haber fases vacías.");
            }
        }
    }
}
