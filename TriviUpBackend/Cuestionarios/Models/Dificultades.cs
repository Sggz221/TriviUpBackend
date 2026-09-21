namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Dificultades permitidas para una pregunta. Se guardan como texto en minúsculas;
/// null significa "sin clasificar".
/// </summary>
public static class Dificultades
{
    public const string Facil = "facil";
    public const string Media = "media";
    public const string Dificil = "dificil";

    public static readonly IReadOnlyList<string> Todas = [Facil, Media, Dificil];

    /// <summary>Texto normalizado (minúsculas, sin espacios) o null si está vacío.</summary>
    public static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToLowerInvariant();

    /// <summary>Válida si está vacía (sin clasificar) o es una de las tres dificultades.</summary>
    public static bool EsValida(string? valor)
    {
        var normalizada = Normalizar(valor);
        return normalizada is null || Todas.Contains(normalizada);
    }

    /// <summary>Normaliza y descarta lo que no sea una dificultad conocida (para borradores, que no bloquean).</summary>
    public static string? NormalizarOSinClasificar(string? valor)
    {
        var normalizada = Normalizar(valor);
        return normalizada is not null && Todas.Contains(normalizada) ? normalizada : null;
    }
}
