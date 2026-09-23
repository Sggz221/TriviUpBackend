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
    /// Pesos de la ruleta para eliminar 0, 1, 2 o 3 respuestas: más probables los valores
    /// medios que los extremos, y 2 más probable que 1.
    /// </summary>
    public static readonly IReadOnlyList<int> PesosRuleta = [10, 30, 45, 15];

    /// <summary>Tira la ruleta: número de respuestas incorrectas a eliminar (0-3).</summary>
    public static int TirarRuleta(Random random)
    {
        var tirada = random.Next(PesosRuleta.Sum());
        for (var i = 0; i < PesosRuleta.Count; i++)
        {
            if (tirada < PesosRuleta[i]) return i;
            tirada -= PesosRuleta[i];
        }
        return PesosRuleta.Count - 1;
    }
}
