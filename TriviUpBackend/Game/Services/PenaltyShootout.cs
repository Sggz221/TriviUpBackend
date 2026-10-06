using TriviUpBackend.Game.Persistence;

namespace TriviUpBackend.Game.Services;

/// <summary>
/// Reglas de la tanda de penaltis, como en el fútbol pero para N jugadores:
/// <list type="bullet">
/// <item>Se tira por turnos en el orden sorteado (A, B, C, A, B, C…), 5 rondas.</item>
/// <item>Tras cada tiro, quien ya no puede alcanzar al líder (goles + tiros que le quedan &lt; goles del líder)
/// queda eliminado; si uno ya no puede ser alcanzado por nadie, gana y la tanda acaba en el acto.</item>
/// <item>Tras las 5 rondas, los empatados con más goles pasan a muerte súbita: una ronda cada vez; al cerrarla,
/// si unos marcan y otros fallan, los que fallan quedan eliminados.</item>
/// </list>
/// </summary>
public static class PenaltyShootout
{
    /// <summary>Tiros por jugador antes de la muerte súbita.</summary>
    public const int RegulationKicks = 5;

    public static int Goals(ShootoutDocument s, long playerId) => s.Kicks.Count(k => k.PlayerId == playerId && k.Scored);

    public static int KicksTaken(ShootoutDocument s, long playerId) => s.Kicks.Count(k => k.PlayerId == playerId);

    /// <summary>Jugadores que aún pueden ganar, en el orden de tiro.</summary>
    public static List<long> Alive(ShootoutDocument s) =>
        s.PlayerIds.Where(id => !s.EliminatedInRound.ContainsKey(id)).ToList();

    /// <summary>Ya se han tirado las 5 rondas: se está en muerte súbita.</summary>
    public static bool InSuddenDeath(ShootoutDocument s) =>
        Alive(s).All(id => KicksTaken(s, id) >= RegulationKicks);

    /// <summary>Ronda en curso (1 = la primera).</summary>
    public static int CurrentRound(ShootoutDocument s)
    {
        var alive = Alive(s);
        return alive.Count == 0 ? 0 : alive.Min(id => KicksTaken(s, id)) + 1;
    }

    /// <summary>Quien tira ahora: el primero (en el orden de tiro) de los vivos que aún no ha tirado esta ronda.</summary>
    public static long? NextKicker(ShootoutDocument s)
    {
        if (s.Finished) return null;
        var alive = Alive(s);
        if (alive.Count == 0) return null;
        var round = CurrentRound(s);
        return alive.First(id => KicksTaken(s, id) == round - 1);
    }

    /// <summary>La siguiente patada abre una ronda (para comprobar que quedan preguntas para todos en la muerte súbita).</summary>
    public static bool AtRoundStart(ShootoutDocument s)
    {
        var alive = Alive(s);
        return alive.Count > 0 && alive.All(id => KicksTaken(s, id) == KicksTaken(s, alive[0]));
    }

    /// <summary>Apunta un tiro y aplica eliminaciones y fin de tanda.</summary>
    public static void RecordKick(ShootoutDocument s, long playerId, bool scored)
    {
        s.Kicks.Add(new PenaltyKickDocument { PlayerId = playerId, Round = KicksTaken(s, playerId) + 1, Scored = scored });
        Evaluate(s);
    }

    /// <summary>Quita de la tanda a un jugador que ya no está (expulsado): cuenta como eliminado.</summary>
    public static void Withdraw(ShootoutDocument s, long playerId)
    {
        if (s.Finished || !s.PlayerIds.Contains(playerId) || s.EliminatedInRound.ContainsKey(playerId)) return;
        s.EliminatedInRound[playerId] = CurrentRound(s);
        Evaluate(s);
    }

    private static void Evaluate(ShootoutDocument s)
    {
        var alive = Alive(s);
        var round = CurrentRound(s);

        if (alive.All(id => KicksTaken(s, id) <= RegulationKicks) && alive.Any(id => KicksTaken(s, id) < RegulationKicks))
        {
            // Fase reglamentaria: fuera quien ya no alcanza al líder aunque marque todo lo que le queda
            int Remaining(long id) => RegulationKicks - KicksTaken(s, id);
            var leader = alive.Max(id => Goals(s, id));
            foreach (var id in alive.Where(id => Goals(s, id) + Remaining(id) < leader))
            {
                s.EliminatedInRound[id] = round;
            }
        }
        else if (AtRoundStart(s))
        {
            // Fin de las 5 rondas o de una ronda de muerte súbita: siguen solo los que más goles llevan
            var best = alive.Max(id => Goals(s, id));
            foreach (var id in alive.Where(id => Goals(s, id) < best))
            {
                s.EliminatedInRound[id] = round - 1;
            }
        }

        alive = Alive(s);
        if (alive.Count == 1)
        {
            Finish(s, alive[0]);
            return;
        }

        // Alguien ya no puede ser alcanzado (solo en la fase reglamentaria, donde quedan tiros contados)
        if (!InSuddenDeath(s))
        {
            foreach (var id in alive)
            {
                var others = alive.Where(o => o != id);
                if (others.All(o => Goals(s, id) > Goals(s, o) + RegulationKicks - KicksTaken(s, o)))
                {
                    foreach (var o in others) s.EliminatedInRound[o] = round;
                    Finish(s, id);
                    return;
                }
            }
        }
    }

    private static void Finish(ShootoutDocument s, long winnerId)
    {
        s.WinnerId = winnerId;
        s.Finished = true;
    }

    /// <summary>
    /// Sin preguntas para seguir: la tanda acaba sin ganador y el 1º se sortea entre los que seguían vivos.
    /// </summary>
    public static void StopOutOfQuestions(ShootoutDocument s)
    {
        s.Finished = true;
        s.OutOfQuestions = true;
    }

    /// <summary>
    /// Orden final del grupo empatado por niveles: cada nivel son jugadores aún empatados entre sí (se sortean).
    /// Primero el ganador (o los vivos si se acabaron las preguntas); después los eliminados, de los que más
    /// aguantaron a los que menos y, a igualdad, por goles.
    /// </summary>
    public static List<List<long>> Tiers(ShootoutDocument s)
    {
        var tiers = new List<List<long>>();
        var alive = Alive(s);
        if (s.WinnerId is { } winner) tiers.Add([winner]);
        else if (alive.Count > 0) tiers.Add(alive);

        tiers.AddRange(s.EliminatedInRound
            .Where(kv => kv.Key != s.WinnerId)
            .GroupBy(kv => (Round: kv.Value, Goals: Goals(s, kv.Key)))
            .OrderByDescending(g => g.Key.Round).ThenByDescending(g => g.Key.Goals)
            .Select(g => g.Select(kv => kv.Key).OrderBy(id => s.PlayerIds.IndexOf(id)).ToList()));
        return tiers;
    }
}
