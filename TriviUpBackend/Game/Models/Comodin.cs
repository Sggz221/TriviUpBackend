namespace TriviUpBackend.Game.Models;

/// <summary>
/// Comodines que cada jugador puede usar durante la partida (por defecto una vez; configurable por sala).
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
    Apuesta,
    /// <summary>Turno propio, solo en modo Presencial: el equipo llama a un amigo y se muestra un cartel hasta que el anfitrión lo quita.</summary>
    Llamada,
    /// <summary>Turno propio: elimina la mitad (redondeando a la baja) de las respuestas incorrectas que quedan.</summary>
    CincuentaCincuenta,
    /// <summary>Turno propio: salta la pregunta sin responderla. No suma ni resta puntos, la pregunta se descarta y pasa el turno.</summary>
    Pasar
}

/// <summary>
/// Reglas fijas de los comodines.
/// </summary>
public static class ComodinReglas
{
    public static readonly IReadOnlyList<ComodinTipo> Todos = Enum.GetValues<ComodinTipo>();

    /// <summary>Comodines que existen en un modo de juego: la Llamada solo en Presencial.</summary>
    public static IEnumerable<ComodinTipo> Disponibles(GameMode modo) =>
        Todos.Where(c => c != ComodinTipo.Llamada || modo == GameMode.Presencial);

    /// <summary>Máximo de usos por partida que el anfitrión puede dar a un comodín.</summary>
    public const int MaxUsos = 99;

    /// <summary>
    /// Comodines activos y sus usos por jugador. <paramref name="config"/> null = los de cada modo, con 1 uso
    /// (también el valor de las sesiones guardadas antes de que existiera la configuración).
    /// </summary>
    public static IReadOnlyDictionary<ComodinTipo, int> Activos(GameMode modo, IReadOnlyDictionary<ComodinTipo, int>? config) =>
        config is null
            ? Disponibles(modo).ToDictionary(c => c, _ => 1)
            : config.Where(kv => kv.Value > 0 && Disponibles(modo).Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// Valida la configuración que llega del cliente: descarta comodines desconocidos o que no existen en el modo
    /// y acota los usos a 1..<see cref="MaxUsos"/>. null = sin configurar (todos, un uso).
    /// </summary>
    public static Dictionary<ComodinTipo, int>? Normalizar(GameMode modo, IReadOnlyDictionary<string, int>? config)
    {
        if (config is null) return null;
        var result = new Dictionary<ComodinTipo, int>();
        foreach (var (nombre, usos) in config)
        {
            if (!Enum.TryParse<ComodinTipo>(nombre, ignoreCase: true, out var tipo) || !Enum.IsDefined(tipo)) continue;
            if (!Disponibles(modo).Contains(tipo) || usos < 1) continue;
            result[tipo] = Math.Min(usos, MaxUsos);
        }
        return result;
    }

    /// <summary>Usos que le quedan a un jugador de cada comodín activo (solo los que aún puede usar).</summary>
    public static Dictionary<ComodinTipo, int> UsosRestantes(
        GameMode modo, IReadOnlyDictionary<ComodinTipo, int>? config, IReadOnlyCollection<ComodinTipo> usados) =>
        Activos(modo, config)
            .Select(kv => (kv.Key, Restantes: kv.Value - usados.Count(u => u == kv.Key)))
            .Where(x => x.Restantes > 0)
            .ToDictionary(x => x.Key, x => x.Restantes);

    /// <summary>true si el comodín se usa durante el turno propio; false si fuera de él.</summary>
    public static bool EsDeTurno(ComodinTipo tipo) => tipo is ComodinTipo.Ruleta or ComodinTipo.DobleONada or ComodinTipo.Llamada or ComodinTipo.CincuentaCincuenta or ComodinTipo.Pasar;

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

    /// <summary>Respuestas incorrectas que elimina el 50/50 de las que quedan: la mitad, redondeando a la baja.</summary>
    public static int EliminadasCincuentaCincuenta(int incorrectasRestantes) => incorrectasRestantes / 2;

    /// <summary>Tira la ruleta: hueco en el que cae y su valor (respuestas incorrectas a eliminar, 0-3).</summary>
    public static (int Hueco, int Valor) TirarRuleta(Random random)
    {
        var hueco = random.Next(HuecosRuleta.Count);
        return (hueco, HuecosRuleta[hueco]);
    }
}
