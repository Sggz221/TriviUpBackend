namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Tipos de pregunta. Se guardan como texto en minúsculas; vacío significa "normal".
/// </summary>
public static class TiposPregunta
{
    /// <summary>Pregunta por turnos: responde el jugador al que le toca.</summary>
    public const string Normal = "normal";

    /// <summary>Nadie tiene turno: el primer equipo en pulsar desde su móvil se lleva la pregunta.</summary>
    public const string Pulsador = "pulsador";

    /// <summary>
    /// Nadie tiene turno: todos imitan un color al azar con tres sliders y el que más se acerca
    /// se lleva la pregunta.
    /// </summary>
    public const string Colores = "colores";

    /// <summary>
    /// Nadie tiene turno: suena una melodía al azar y el primero que la toca con los botones de la ocarina
    /// se lleva la pregunta.
    /// </summary>
    public const string Ocarina = "ocarina";

    public static readonly IReadOnlyList<string> Todos = [Normal, Pulsador, Colores, Ocarina];

    /// <summary>Preguntas sin turno: se las lleva quien gana el pulsador, la prueba de colores o la ocarina.</summary>
    public static bool SinTurno(string? tipo) => tipo is Pulsador or Colores or Ocarina;

    /// <summary>Texto normalizado (minúsculas, sin espacios) o null si está vacío.</summary>
    public static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToLowerInvariant();

    /// <summary>Válido si está vacío (normal) o es uno de los tipos conocidos.</summary>
    public static bool EsValido(string? valor)
    {
        var normalizado = Normalizar(valor);
        return normalizado is null || Todos.Contains(normalizado);
    }

    /// <summary>Normaliza y convierte en normal lo que no sea un tipo conocido (para borradores, que no bloquean).</summary>
    public static string NormalizarONormal(string? valor)
    {
        var normalizado = Normalizar(valor);
        return normalizado is not null && Todos.Contains(normalizado) ? normalizado : Normal;
    }
}
