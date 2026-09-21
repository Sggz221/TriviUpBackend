using System.Text.RegularExpressions;

namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Color configurable de una fase, en formato #rrggbb. Se guarda en minúsculas;
/// null significa "color por defecto" (el cliente elige uno de su paleta).
/// </summary>
public static partial class FaseColores
{
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex FormatoRegex();

    /// <summary>Válido si está vacío (color por defecto) o es #rrggbb.</summary>
    public static bool EsValido(string? valor) =>
        string.IsNullOrWhiteSpace(valor) || FormatoRegex().IsMatch(valor.Trim());

    /// <summary>Minúsculas y sin espacios, o null si está vacío.</summary>
    public static string? Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToLowerInvariant();

    /// <summary>Normaliza y descarta lo que no sea #rrggbb (para borradores, que no bloquean).</summary>
    public static string? NormalizarOSinColor(string? valor)
    {
        var normalizado = Normalizar(valor);
        return normalizado is not null && FormatoRegex().IsMatch(normalizado) ? normalizado : null;
    }
}
