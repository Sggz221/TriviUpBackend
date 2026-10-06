using TriviUpBackend.Game.DTOs;
using TriviUpBackend.Game.Persistence;

namespace TriviUpBackend.Game.Services;

/// <summary>
/// Clasificación final con desempates. Solo se desempata el podio: los empatados por el 1º, 2º o 3º (también un
/// empate entre 3º y 4º) acaban con puestos distintos; del 4º hacia abajo comparten puesto. El 1º lo decide la tanda
/// de penaltis si la hubo; el resto (y lo que quede empatado tras la tanda) se sortea: moneda con 2, ruleta con 3+.
/// </summary>
public static class FinalRanking
{
    public const string Penaltis = "penaltis";
    public const string Moneda = "moneda";
    public const string Ruleta = "ruleta";

    /// <summary>Puestos del podio que se desempatan.</summary>
    public const int PodiumSize = 3;

    /// <summary>Jugadores empatados por el 1º (vacío si el 1º está claro).</summary>
    public static List<PlayerDocument> TiedForFirst(IReadOnlyList<PlayerDocument> players)
    {
        if (players.Count < 2) return [];
        var best = players.Max(p => p.Score);
        var tied = players.Where(p => p.Score == best).ToList();
        return tied.Count > 1 ? tied : [];
    }

    public static (List<PlayerResultDto> Results, List<TiebreakDto> Tiebreaks) Build(
        IReadOnlyList<PlayerDocument> players, ShootoutDocument? shootout, Random random)
    {
        var byId = players.ToDictionary(p => p.UserId);
        var ranks = new List<(PlayerDocument Player, int Rank)>();
        var tiebreaks = new List<TiebreakDto>();
        var playedShootout = shootout is { Started: true, Finished: true };

        if (playedShootout)
        {
            tiebreaks.Add(new TiebreakDto(Penaltis, 1, shootout!.PlayerIds, Names(shootout.PlayerIds, byId),
                Kicks: shootout.Kicks.Select(k => new PenaltyKickDto(k.PlayerId, k.Round, k.Scored)).ToList(),
                WinnerId: shootout.WinnerId, OutOfQuestions: shootout.OutOfQuestions));
        }

        var position = 1;
        foreach (var group in players.GroupBy(p => p.Score).OrderByDescending(g => g.Key))
        {
            var ids = group.Select(p => p.UserId).ToList();
            if (ids.Count > 1 && position <= PodiumSize)
            {
                var tiers = position == 1 && playedShootout && ids.All(shootout!.PlayerIds.Contains)
                    ? PenaltyShootout.Tiers(shootout!).Select(t => t.Where(ids.Contains).ToList()).Where(t => t.Count > 0).ToList()
                    : [ids];

                var pos = position;
                foreach (var tier in tiers)
                {
                    var ordered = tier;
                    if (tier.Count > 1 && pos <= PodiumSize)
                    {
                        // Sorteo: el orden sale de aquí y los clientes solo lo animan
                        ordered = tier.OrderBy(_ => random.Next()).ToList();
                        tiebreaks.Add(new TiebreakDto(tier.Count == 2 ? Moneda : Ruleta, pos, ordered, Names(ordered, byId),
                            Picks: Math.Min(tier.Count, PodiumSize - pos + 1)));
                    }
                    for (var k = 0; k < ordered.Count; k++)
                    {
                        var p = pos + k;
                        // Dentro del podio, puestos distintos; fuera, los empatados comparten el primer puesto libre
                        var rank = p <= PodiumSize ? p : tier.Count > 1 && pos <= PodiumSize ? PodiumSize + 1 : pos;
                        ranks.Add((byId[ordered[k]], rank));
                    }
                    pos += ordered.Count;
                }
            }
            else
            {
                ranks.AddRange(group.Select(p => (p, position)));
            }
            position += ids.Count;
        }

        var results = ranks
            .OrderBy(r => r.Rank).ThenByDescending(r => r.Player.Score)
            .Select(r => new PlayerResultDto(
                r.Player.UserId,
                r.Player.Username,
                r.Rank,
                r.Player.Score,
                r.Player.CorrectAnswers,
                r.Player.WrongAnswers,
                r.Player.CorrectAnswers + r.Player.WrongAnswers > 0
                    ? (int)Math.Round((double)r.Player.CorrectAnswers / (r.Player.CorrectAnswers + r.Player.WrongAnswers) * 100)
                    : 0))
            .ToList();
        return (results, tiebreaks);
    }

    private static List<string> Names(IEnumerable<long> ids, IReadOnlyDictionary<long, PlayerDocument> byId) =>
        ids.Select(id => byId.TryGetValue(id, out var p) ? p.Username : "jugador").ToList();
}
