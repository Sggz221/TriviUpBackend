namespace TriviUpBackend.Game.Models;

/// <summary>
/// Comodines que cada jugador puede usar una vez por partida.
/// </summary>
public enum ComodinTipo
{
    /// <summary>Turno propio: elimina entre 0 y 3 respuestas incorrectas al azar.</summary>
    Ruleta,
    /// <summary>Turno propio: acierto = doble del valor de una pregunta; fallo = pierde una pregunta.</summary>
    DobleONada,
    /// <summary>Fuera de turno: responde la pregunta de quien tiene el turno.</summary>
    Robo,
    /// <summary>Fuera de turno: apuesta si el jugador en turno acertará o fallará.</summary>
    Apuesta
}

/// <summary>
/// Reglas fijas de los comodines.
/// </summary>
public static class ComodinReglas
{
    public static readonly IReadOnlyList<ComodinTipo> Todos = Enum.GetValues<ComodinTipo>();

    /// <summary>true si el comodín se usa durante el turno propio; false si fuera de él.</summary>
    public static bool EsDeTurno(ComodinTipo tipo) => tipo is ComodinTipo.Ruleta or ComodinTipo.DobleONada;

    /// <summary>
    /// Huecos de la ruleta en orden horario desde arriba; cada uno dice cuántas respuestas
    /// incorrectas elimina. Todos miden lo mismo, así que la probabilidad sale del número de
    /// huecos: 0 → 2/20, 1 → 8/20, 2 → 8/20, 3 → 2/20. Mezclados para que no se vea venir
    /// dónde cae. El frontend dibuja exactamente esta lista (game-room.ts, RULETA_HUECOS).
    /// </summary>
    public static readonly IReadOnlyList<int> HuecosRuleta =
        [1, 2, 0, 1, 2, 1, 2, 3, 1, 2, 1, 2, 0, 1, 2, 1, 2, 3, 1, 2];

    /// <summary>
    /// Lo que dura la animación de la ruleta (~15 s de giro + 2 s mostrando el resultado). El turno de quien la usa
    /// se alarga este tiempo para que el giro no le coma segundos.
    /// </summary>
    public const int DuracionRuletaMs = 17000;

    /// <summary>Tira la ruleta: hueco en el que cae y su valor (respuestas incorrectas a eliminar, 0-3).</summary>
    public static (int Hueco, int Valor) TirarRuleta(Random random)
    {
        var hueco = random.Next(HuecosRuleta.Count);
        return (hueco, HuecosRuleta[hueco]);
    }
}
