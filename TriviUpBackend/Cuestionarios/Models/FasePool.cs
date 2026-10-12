using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TriviUpBackend.Cuestionarios.Entities;

/// <summary>
/// Fase cuyas preguntas no son fijas: en cada partida se juegan <see cref="Cantidad"/> preguntas del banco
/// de quien crea la sala, sacadas de una en una al jugarlas (ver Game.Services.BankDrawer), elegidas a mano o
/// por filtros. Una fase con pool no tiene preguntas propias; ocupa su número de fase igual que las demás.
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

    /// <summary>
    /// De las <see cref="Cantidad"/> preguntas, cuántas son de pulsador. Igual que <see cref="Colores"/> y
    /// <see cref="Ocarina"/>: cada una sale del banco entre las preguntas de ese tipo. El resto son normales.
    /// </summary>
    public int Pulsador { get; init; }

    public int Colores { get; init; }

    public int Ocarina { get; init; }

    /// <summary>Preguntas normales (con turno) que saca el pool.</summary>
    [JsonIgnore]
    public int Normales => Math.Max(Cantidad - Pulsador - Colores - Ocarina, 0);

    /// <summary>Cuántas preguntas de cada tipo saca el pool (solo los tipos con alguna).</summary>
    public IEnumerable<(string Tipo, int Cantidad)> CantidadesPorTipo() =>
        new[]
        {
            (TiposPregunta.Normal, Normales), (TiposPregunta.Pulsador, Pulsador),
            (TiposPregunta.Colores, Colores), (TiposPregunta.Ocarina, Ocarina)
        }.Where(t => t.Item2 > 0);
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

    /// <summary>Tipo, solo para mostrarlo en el builder; al validar y al jugar manda el del banco.</summary>
    [MaxLength(20)]
    public string? Tipo { get; init; }
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
        var cantidad = Math.Clamp(pool.Cantidad, 1, CantidadMaxima);
        // Las especiales no pueden pasar del total: se recortan en orden pulsador, colores, ocarina
        var pulsador = Math.Clamp(pool.Pulsador, 0, cantidad);
        var colores = Math.Clamp(pool.Colores, 0, cantidad - pulsador);
        var ocarina = Math.Clamp(pool.Ocarina, 0, cantidad - pulsador - colores);
        return pool with
        {
            FaseNombre = string.IsNullOrWhiteSpace(pool.FaseNombre) ? null : pool.FaseNombre.Trim(),
            FaseColor = FaseColores.NormalizarOSinColor(pool.FaseColor),
            Cantidad = cantidad,
            Pulsador = pulsador,
            Colores = colores,
            Ocarina = ocarina,
            Origen = origen,
            Preguntas = origen == Manual
                ? pool.Preguntas.Where(p => p.Id > 0).DistinctBy(p => p.Id)
                    .Select(p => p with { Tipo = TiposPregunta.NormalizarONormal(p.Tipo) }).ToList()
                : new(),
            CategoriaId = origen == Filtros ? pool.CategoriaId : null,
            CategoriaNombre = origen == Filtros && pool.CategoriaId is not null ? pool.CategoriaNombre?.Trim() : null,
            Dificultad = origen == Filtros ? Dificultades.NormalizarOSinClasificar(pool.Dificultad) : null
        };
    }
}
