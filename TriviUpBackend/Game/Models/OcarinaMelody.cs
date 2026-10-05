namespace TriviUpBackend.Game.Models;

/// <summary>Figura de una nota (para dibujarla y para su duración al tocarla).</summary>
public enum NoteFigure
{
    Semicorchea,
    Corchea,
    Negra,
    Blanca
}

/// <summary>
/// Nota de la melodía de la ocarina. <see cref="Pitch"/> es el botón (0-4) de las cinco notas estilo Zelda:
/// 0 = Re (A), 1 = Fa (▼), 2 = La (▶), 3 = Si (◀), 4 = Re agudo (▲).
/// </summary>
public sealed record OcarinaNote(int Pitch, NoteFigure Figure);

/// <summary>
/// Reglas de la pregunta especial Ocarina: suena una melodía al azar, lenta y una sola vez; después el primero
/// que la reproduce con los botones se lleva la pregunta. Solo cuentan las notas (no el ritmo).
/// </summary>
public static class OcarinaMelody
{
    public const int PitchCount = 5;
    public const int Length = 6;

    /// <summary>Pausa antes de la primera nota, para que dé tiempo a cargar el audio en los clientes.</summary>
    public const int LeadInMs = 1500;

    /// <summary>Duración de una negra: tempo lento (72 bpm) para que se pueda memorizar.</summary>
    public const int QuarterMs = 833;

    /// <summary>Melodía al azar: nunca tres notas iguales seguidas, y la última nota es larga (blanca).</summary>
    public static List<OcarinaNote> Random(Random random)
    {
        var notes = new List<OcarinaNote>(Length);
        for (var i = 0; i < Length; i++)
        {
            int pitch;
            do
            {
                pitch = random.Next(PitchCount);
            } while (i >= 2 && notes[i - 1].Pitch == pitch && notes[i - 2].Pitch == pitch);

            var figure = i == Length - 1 ? NoteFigure.Blanca : RandomFigure(random);
            notes.Add(new OcarinaNote(pitch, figure));
        }
        return notes;
    }

    /// <summary>Mayoría de negras, con algunas corcheas y alguna semicorchea o blanca para que tenga ritmo.</summary>
    private static NoteFigure RandomFigure(Random random) => random.Next(100) switch
    {
        < 50 => NoteFigure.Negra,
        < 80 => NoteFigure.Corchea,
        < 90 => NoteFigure.Semicorchea,
        _ => NoteFigure.Blanca
    };

    public static int DurationMs(NoteFigure figure) => figure switch
    {
        NoteFigure.Semicorchea => QuarterMs / 4,
        NoteFigure.Corchea => QuarterMs / 2,
        NoteFigure.Negra => QuarterMs,
        _ => QuarterMs * 2
    };

    /// <summary>Lo que tarda en sonar la melodía completa, desde que empieza la pregunta.</summary>
    public static int PlaybackMs(IEnumerable<OcarinaNote> melody) => LeadInMs + melody.Sum(n => DurationMs(n.Figure));

    /// <summary>Acierto si las notas coinciden en orden (el ritmo no cuenta).</summary>
    public static bool Matches(IReadOnlyList<OcarinaNote> melody, IReadOnlyList<int> attempt) =>
        attempt.Count == melody.Count && melody.Select(n => n.Pitch).SequenceEqual(attempt);
}
