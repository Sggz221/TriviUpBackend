using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Cuestionarios.Repositories;

namespace TriviUpBackend.Game.Services;

/// <summary>
/// Sortea, al empezar una partida, las preguntas de las fases con pool a partir del banco del
/// autor del cuestionario.
/// </summary>
public static class PoolDrawer
{
    /// <summary>
    /// Devuelve las preguntas sorteadas de todos los pools, ya con los datos de su fase. Si el
    /// banco no tiene suficientes, el pool aporta las que haya (un pool vacío no aporta ninguna).
    /// Una misma pregunta del banco no sale dos veces en la partida. Los ids son negativos
    /// (-id del banco) para no chocar con los de las preguntas fijas del cuestionario.
    /// </summary>
    public static async Task<List<Pregunta>> DrawAsync(Quiz quiz, IBancoPreguntaRepository banco, Random random)
    {
        var resultado = new List<Pregunta>();
        var usadas = new HashSet<long>();

        foreach (var pool in quiz.Pools.OrderBy(p => p.FaseNumero))
        {
            var candidatas = pool.Origen == OrigenesPool.Manual
                ? pool.Preguntas.Select(p => p.Id).Distinct().ToList()
                : await banco.FindIdsAsync(quiz.CreatorId, pool.CategoriaId, pool.Dificultad);

            // Se baraja todo y se cargan solo las necesarias; las que no sirvan (borradas del banco
            // o sin una única correcta) se cubren con las siguientes.
            var orden = candidatas.Where(id => !usadas.Contains(id)).OrderBy(_ => random.Next()).ToList();
            var cargadas = orden.Count == 0
                ? new List<BancoPregunta>()
                : await banco.FindByIdsAsync(quiz.CreatorId, orden);
            var porId = cargadas.Where(EsJugable).ToDictionary(p => p.Id);

            foreach (var id in orden.Where(porId.ContainsKey).Take(pool.Cantidad))
            {
                usadas.Add(id);
                resultado.Add(ToPregunta(porId[id], quiz, pool));
            }
        }

        return resultado;
    }

    /// <summary>Al menos dos respuestas con texto y exactamente una correcta.</summary>
    private static bool EsJugable(BancoPregunta p)
    {
        var respuestas = p.Respuestas;
        return !string.IsNullOrWhiteSpace(p.Enunciado) && respuestas.Count >= 2 &&
               respuestas.All(r => !string.IsNullOrWhiteSpace(r.Texto)) &&
               respuestas.Count(r => r.EsCorrecta) == 1;
    }

    private static Pregunta ToPregunta(BancoPregunta p, Quiz quiz, FasePool pool) => new()
    {
        Id = -p.Id,
        QuizId = quiz.Id,
        CreatorId = quiz.CreatorId,
        Enunciado = p.Enunciado,
        ImagenUrl = p.ImagenUrl,
        Dificultad = p.Dificultad,
        Tipo = TiposPregunta.Normal,
        FaseNumero = pool.FaseNumero,
        FaseNombre = pool.FaseNombre,
        FaseColor = pool.FaseColor,
        Respuestas = p.Respuestas.Select((r, i) => new Respuesta
        {
            Id = -(p.Id * 10 + i),
            Texto = r.Texto,
            EsCorrecta = r.EsCorrecta
        }).ToList()
    };
}
