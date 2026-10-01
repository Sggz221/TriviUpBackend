using System.ComponentModel.DataAnnotations;

namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Fase cuyas preguntas no son fijas: en cada partida se sortean <see cref="Cantidad"/> preguntas
/// del banco del autor del cuestionario, elegidas a mano o por filtros. Una fase con pool no tiene
/// preguntas propias; ocupa su número de fase igual que las demás.
/// </summary>
public record FasePool
{
    /// <summary>Número de la fase (1..n), compartido con las fases de preguntas fijas.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "La fase debe ser mayor a 0")]
    public int FaseNumero { get; init; } = 1;

    [MaxLength(100, ErrorMessage = "El nombre de la fase no puede exceder 100 caracteres")]
    public string? FaseNombre { get; init; }

    [MaxLength(7, ErrorMessage = "Color de fase no válido")]
    public string? FaseColor { get; init; }

    /// <summary>Preguntas que salen en cada partida.</summary>
    public int Cantidad { get; init; } = OrigenesPool.CantidadPorDefecto;

    /// <summary>De dónde salen: elegidas a mano (<see cref="Preguntas"/>) o por filtros.</summary>
    [MaxLength(10)]
    public string Origen { get; init; } = OrigenesPool.Filtros;

    /// <summary>Origen manual: preguntas del banco entre las que se sortea (enlazadas por id).</summary>
    public List<FasePoolPregunta> Preguntas { get; init; } = new();

    /// <summary>Origen por filtros: categoría del banco (null = cualquiera).</summary>
    public long? CategoriaId { get; init; }

    /// <summary>Nombre de la categoría, solo para mostrarlo.</summary>
    [MaxLength(BancoCategoria.NombreMaxLength)]
    public string? CategoriaNombre { get; init; }

    /// <summary>Origen por filtros: dificultad (null = cualquiera).</summary>
    [MaxLength(10)]
    public string? Dificultad { get; init; }
}

/// <summary>
/// Pregunta del banco elegida a mano para un pool. El enunciado es solo para mostrarlo en el
/// builder; en la partida se usa siempre la versión actual del banco.
/// </summary>
public record FasePoolPregunta
{
    public long Id { get; init; }

    [MaxLength(1000)]
    public string Enunciado { get; init; } = string.Empty;
}

/// <summary>Orígenes de un pool y sus límites.</summary>
public static class OrigenesPool
{
    public const string Manual = "manual";
    public const string Filtros = "filtros";

    public const int CantidadPorDefecto = 5;
    public const int CantidadMaxima = 50;

    public static readonly IReadOnlyList<string> Todos = [Manual, Filtros];

    public static bool EsValido(string? valor) =>
        valor is not null && Todos.Contains(valor.Trim().ToLowerInvariant());

    /// <summary>
    /// Deja el pool listo para guardar: textos recortados, color y dificultad normalizados,
    /// cantidad dentro de límites y preguntas manuales sin repetir. No rechaza nada (eso lo hace
    /// la validación al publicar), para que los borradores siempre se puedan guardar.
    /// </summary>
    public static FasePool Normalizar(FasePool pool)
    {
        var origen = EsValido(pool.Origen) ? pool.Origen.Trim().ToLowerInvariant() : Filtros;
        return pool with
        {
            FaseNombre = string.IsNullOrWhiteSpace(pool.FaseNombre) ? null : pool.FaseNombre.Trim(),
            FaseColor = FaseColores.NormalizarOSinColor(pool.FaseColor),
            Cantidad = Math.Clamp(pool.Cantidad, 1, CantidadMaxima),
            Origen = origen,
            Preguntas = origen == Manual
                ? pool.Preguntas.Where(p => p.Id > 0).DistinctBy(p => p.Id).ToList()
                : new(),
            CategoriaId = origen == Filtros ? pool.CategoriaId : null,
            CategoriaNombre = origen == Filtros && pool.CategoriaId is not null ? pool.CategoriaNombre?.Trim() : null,
            Dificultad = origen == Filtros ? Dificultades.NormalizarOSinClasificar(pool.Dificultad) : null
        };
    }
}
