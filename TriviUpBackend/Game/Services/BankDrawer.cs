using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;
using TriviUpBackend.Game.Persistence;

namespace TriviUpBackend.Game.Services;

/// <summary>
/// Saca preguntas del banco de quien crea la sala, de una en una y en el momento de jugarlas: las fases con pool
/// no se sortean al empezar la partida. Una pregunta del banco que ya ha salido en la partida (jugada o cambiada
/// con un comodín) no vuelve a salir. Los ids de las preguntas del banco en la partida son negativos (-id del
/// banco) para no chocar con los de las preguntas fijas del cuestionario.
/// </summary>
public static class BankDrawer
{
    /// <summary>Cuántas preguntas se cargan de cada vez al buscar una jugable (las rotas se saltan).</summary>
    private const int LoadBatch = 10;

    /// <summary>
    /// Reglas de un pool para la partida. Si la sala la crea el autor del cuestionario, valen tal cual (preguntas
    /// elegidas a mano o filtros por categoría y dificultad de su banco). Si la crea otro usuario, esas preguntas y
    /// categorías no son de su banco: se usa todo su banco, filtrando solo por la dificultad del pool.
    /// </summary>
    public static PoolSpecDocument ToSpec(FasePool pool, bool bancoDelAutor) => new()
    {
        FaseNumero = pool.FaseNumero,
        FaseNombre = pool.FaseNombre,
        FaseColor = pool.FaseColor,
        Cantidad = pool.Cantidad,
        CandidateIds = bancoDelAutor && pool.Origen == OrigenesPool.Manual
            ? pool.Preguntas.Select(p => p.Id).Distinct().ToList()
            : null,
        CategoriaId = bancoDelAutor && pool.Origen == OrigenesPool.Filtros ? pool.CategoriaId : null,
        Dificultad = pool.Origen == OrigenesPool.Filtros ? pool.Dificultad : null
    };

    /// <summary>Ids del banco que cumplen las reglas del pool (null = cualquiera del banco) y aún no han salido.</summary>
    public static async Task<List<long>> AvailableIdsAsync(
        IBancoPreguntaRepository banco, long bancoOwnerId, PoolSpecDocument? spec, IReadOnlyCollection<long> used)
    {
        var ids = await banco.FindIdsAsync(bancoOwnerId, spec?.CategoriaId, spec?.Dificultad);
        // Elegidas a mano: solo las que siguen en el banco (las borradas no cuentan)
        if (spec?.CandidateIds is { } elegidas) ids = ids.Intersect(elegidas).ToList();
        return ids.Where(id => !used.Contains(id)).Distinct().ToList();
    }

    /// <summary>
    /// Una pregunta jugable al azar del banco que cumpla las reglas del pool y no haya salido en la partida, o null
    /// si no queda ninguna. Solo se cargan de la base de datos las que hacen falta.
    /// </summary>
    public static async Task<BancoPregunta?> DrawAsync(
        IBancoPreguntaRepository banco, long bancoOwnerId, PoolSpecDocument? spec, IReadOnlyCollection<long> used, Random random)
    {
        var orden = (await AvailableIdsAsync(banco, bancoOwnerId, spec, used)).OrderBy(_ => random.Next()).ToList();
        for (var i = 0; i < orden.Count; i += LoadBatch)
        {
            var lote = orden.Skip(i).Take(LoadBatch).ToList();
            var cargadas = (await banco.FindByIdsAsync(bancoOwnerId, lote)).Where(EsJugable).ToDictionary(p => p.Id);
            var elegida = lote.FirstOrDefault(cargadas.ContainsKey);
            if (elegida != 0) return cargadas[elegida];
        }
        return null;
    }

    /// <summary>Al menos dos respuestas con texto y exactamente una correcta.</summary>
    public static bool EsJugable(BancoPregunta p)
    {
        var respuestas = p.Respuestas;
        return !string.IsNullOrWhiteSpace(p.Enunciado) && respuestas.Count >= 2 &&
               respuestas.All(r => !string.IsNullOrWhiteSpace(r.Texto)) &&
               respuestas.Count(r => r.EsCorrecta) == 1;
    }

    /// <summary>
    /// Rellena el hueco <paramref name="slot"/> (su fase y su pool se conservan) con la pregunta del banco, con las
    /// respuestas barajadas.
    /// </summary>
    public static QuestionSnapshot Fill(QuestionSnapshot slot, BancoPregunta p, Random random) => new()
    {
        Id = -p.Id,
        Enunciado = p.Enunciado,
        ImagenUrl = p.ImagenUrl,
        Curiosidad = p.Curiosidad,
        FaseNumero = slot.FaseNumero,
        FaseNombre = slot.FaseNombre,
        FaseColor = slot.FaseColor,
        Dificultad = p.Dificultad,
        PoolIndex = slot.PoolIndex,
        EsPenalti = slot.EsPenalti,
        Respuestas = p.Respuestas
            .Select((r, i) => new AnswerSnapshot { Id = -(p.Id * 10 + i), Texto = r.Texto, EsCorrecta = r.EsCorrecta })
            .OrderBy(_ => random.Next())
            .ToList()
    };

    /// <summary>Hueco de una fase con pool: se rellena con una pregunta del banco cuando le toca jugarse.</summary>
    public static QuestionSnapshot Slot(PoolSpecDocument spec, int poolIndex) => new()
    {
        FaseNumero = spec.FaseNumero,
        FaseNombre = spec.FaseNombre,
        FaseColor = spec.FaseColor,
        PoolIndex = poolIndex
    };
}
