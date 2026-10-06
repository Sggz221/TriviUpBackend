using TriviUpBackend.Game.DTOs;
using TriviUpBackend.Game.Models;

namespace TriviUpBackend.Game.Persistence;

/// <summary>
/// Documento serializable de una sesión de partida (sin entidades EF).
/// </summary>
public sealed class GameSessionDocument
{
    public string RoomCode { get; set; } = string.Empty;
    public long QuizId { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public long OwnerId { get; set; }
    public GameState State { get; set; } = GameState.Waiting;
    public List<PlayerDocument> Players { get; set; } = new();
    public List<long> TurnQueue { get; set; } = new();
    public int CurrentQuestionIndex { get; set; }
    public List<QuestionSnapshot> Questions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime? TurnStartedAt { get; set; }
    public int? PausedTimeRemaining { get; set; }
    public long TurnGeneration { get; set; }
    public long? TurnDeadlineUnixMs { get; set; }
    public long Revision { get; set; }

    /// <summary>Segundos por turno elegidos al crear la sala. null = valor por defecto; 0 = sin tiempo.</summary>
    public int? TurnTimeLimitSeconds { get; set; }

    public GameMode Mode { get; set; } = GameMode.Normal;

    /// <summary>Comodines activos y usos por jugador, elegidos al crear la sala (null = los del modo, un uso).</summary>
    public Dictionary<ComodinTipo, int>? ComodinUsos { get; set; }

    // ---- Modo presencial: respuesta de la pregunta en curso (se reinicia en cada pregunta) ----

    /// <summary>Opción marcada por el anfitrión y aún sin confirmar (null = ninguna).</summary>
    public int? MarkedAnswerIndex { get; set; }

    /// <summary>Respuesta ya confirmada: el resultado está en pantalla hasta que el anfitrión pase de pregunta.</summary>
    public bool AwaitingNextQuestion { get; set; }

    /// <summary>Resultado de la pregunta confirmada, para reenviarlo a quien se reconecta mientras se espera.</summary>
    public TurnResultDto? LastTurnResult { get; set; }

    // ---- Estado de comodines de la pregunta en curso (se reinicia en cada pregunta) ----

    /// <summary>Jugador que robó la pregunta actual (null si nadie). Se mantiene aunque falle el robo.</summary>
    public long? StolenById { get; set; }

    /// <summary>true mientras el ladrón está respondiendo; false cuando el turno vuelve al original.</summary>
    public bool StealActive { get; set; }

    /// <summary>Tanda de penaltis para desempatar el 1º puesto al acabar las preguntas (null si no hay).</summary>
    public ShootoutDocument? Shootout { get; set; }

    /// <summary>Índices de respuestas eliminadas por la ruleta en la pregunta actual.</summary>
    public List<int> EliminatedAnswerIndexes { get; set; } = new();

    /// <summary>Jugadores con "Doble o nada" activo en la pregunta actual.</summary>
    public List<long> DoubleOrNothingPlayers { get; set; } = new();

    /// <summary>Apuestas sobre el jugador en turno en la pregunta actual.</summary>
    public List<BetDocument> Bets { get; set; } = new();

    /// <summary>Se ha usado algún comodín en la pregunta actual: a partir de ahí no se puede robar.</summary>
    public bool ComodinUsedOnQuestion { get; set; }

    /// <summary>Jugador al que se le oculta el texto de las respuestas en la pregunta actual (null = nadie).</summary>
    public long? TextHiddenForPlayerId { get; set; }

    /// <summary>Llamada en curso: el cartel sigue en pantalla hasta que el anfitrión lo quita.</summary>
    public bool CallActive { get; set; }

    // ---- Ronda dinámica: pulsador (se reinicia en cada pregunta) ----

    /// <summary>Pregunta dinámica a la espera de que algún equipo pulse: todavía no responde nadie.</summary>
    public bool BuzzerOpen { get; set; }

    /// <summary>Equipo que pulsó primero y responde la pregunta dinámica (null mientras el pulsador está abierto).</summary>
    public long? BuzzWinnerId { get; set; }

    // ---- Pregunta de Colores: prueba de imitar el color (se reinicia en cada pregunta) ----

    /// <summary>Prueba de colores en curso: todavía no responde nadie.</summary>
    public bool ColorOpen { get; set; }

    /// <summary>Color a imitar en la pregunta de colores actual (al azar en cada pregunta).</summary>
    public ColorHsb? ColorTarget { get; set; }

    /// <summary>Colores enviados en la prueba actual (uno por jugador).</summary>
    public List<ColorGuessDocument> ColorGuesses { get; set; } = new();

    // ---- Pregunta de Ocarina: tocar la melodía (se reinicia en cada pregunta) ----

    /// <summary>Prueba de ocarina en curso: todavía no responde nadie.</summary>
    public bool OcarinaOpen { get; set; }

    /// <summary>Melodía a tocar en la pregunta de ocarina actual (al azar en cada pregunta).</summary>
    public List<OcarinaNote>? OcarinaMelody { get; set; }

    /// <summary>Hasta cuándo suena la melodía: antes no se aceptan intentos.</summary>
    public long? OcarinaListenUntilUnixMs { get; set; }

    /// <summary>Cuándo se puede empezar a pulsar (tras el banner y la cuenta atrás); antes se rechaza.</summary>
    public long? BuzzerOpensAtUnixMs { get; set; }

    /// <summary>Lo que le quedaba a la cuenta atrás del pulsador al pausar (se reanuda desde ahí).</summary>
    public int? PausedBuzzerLockMs { get; set; }

    /// <summary>Pregunta sin turno a la espera de ganador (pulsador, colores u ocarina abiertos).</summary>
    public bool IsWaitingForWinner => BuzzerOpen || ColorOpen || OcarinaOpen;

    /// <summary>
    /// Jugador al que le toca la pregunta: el que ganó el pulsador o la prueba de colores en una pregunta
    /// sin turno o, si no, el de la cola de turnos.
    /// </summary>
    public long? GetCurrentPlayerId() =>
        IsWaitingForWinner ? null : BuzzWinnerId ?? (TurnQueue.Count > 0 ? TurnQueue[0] : null);

    /// <summary>Quien responde ahora: el ladrón durante un robo o, si no, el jugador en turno.</summary>
    public long? GetAnsweringPlayerId() => StealActive && StolenById.HasValue ? StolenById : GetCurrentPlayerId();

    /// <summary>La pregunta en curso no tiene turno (pulsador o colores): sin comodines ni cola.</summary>
    public bool IsCurrentQuestionDynamic() =>
        CurrentQuestionIndex < Questions.Count && Questions[CurrentQuestionIndex].SinTurno;

    public void ResetQuestionState()
    {
        StolenById = null;
        StealActive = false;
        EliminatedAnswerIndexes = new();
        DoubleOrNothingPlayers = new();
        Bets = new();
        ComodinUsedOnQuestion = false;
        TextHiddenForPlayerId = null;
        CallActive = false;
        BuzzerOpen = false;
        BuzzWinnerId = null;
        ColorOpen = false;
        ColorTarget = null;
        ColorGuesses = new();
        OcarinaOpen = false;
        OcarinaMelody = null;
        OcarinaListenUntilUnixMs = null;
        BuzzerOpensAtUnixMs = null;
        PausedBuzzerLockMs = null;
        MarkedAnswerIndex = null;
        AwaitingNextQuestion = false;
        LastTurnResult = null;
    }

    public long? RotateTurn()
    {
        if (TurnQueue.Count == 0) return null;
        var current = TurnQueue[0];
        TurnQueue.RemoveAt(0);
        TurnQueue.Add(current);
        return TurnQueue[0];
    }
}

public sealed class PlayerDocument
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public int Score { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int TurnPosition { get; set; } = -1;
    public bool IsConnected { get; set; } = true;
    public bool IsOwner { get; set; }

    /// <summary>
    /// Espectador asignado por el anfitrión en el lobby: ve la partida pero no juega
    /// (fuera de la cola de turnos, puntuaciones y leaderboard final).
    /// </summary>
    public bool IsSpectator { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Momento en que se desconectó (refresh, wifi, cerrar pestaña). Null mientras
    /// está conectado. Se usa para dar al owner una ventana de gracia antes de
    /// transferir el ownership a otro jugador de verdad.
    /// </summary>
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>Usos de comodín ya gastados: un tipo repetido cuenta cada uso.</summary>
    public List<ComodinTipo> UsedComodines { get; set; } = new();

    public bool CanPlay() => !IsOwner && !IsSpectator;

    public List<ComodinTipo> AvailableComodines(GameMode mode, IReadOnlyDictionary<ComodinTipo, int>? config = null) =>
        CanPlay() ? ComodinReglas.UsosRestantes(mode, config, UsedComodines).Keys.ToList() : new();

    /// <summary>Usos restantes por comodín (solo los que aún puede usar).</summary>
    public Dictionary<ComodinTipo, int> RemainingUses(GameMode mode, IReadOnlyDictionary<ComodinTipo, int>? config = null) =>
        CanPlay() ? ComodinReglas.UsosRestantes(mode, config, UsedComodines) : new();
}

public sealed class ColorGuessDocument
{
    public long UserId { get; set; }
    public ColorHsb Color { get; set; } = new(0, 0, 0);
}

/// <summary>
/// Tanda de penaltis (reglas en <see cref="Services.PenaltyShootout"/>). Cada tiro es una pregunta del banco del
/// creador del cuestionario: acierto = gol. No suma puntos; solo decide el desempate.
/// </summary>
public sealed class ShootoutDocument
{
    /// <summary>Jugadores empatados, en el orden de tiro (sorteado al crear la tanda).</summary>
    public List<long> PlayerIds { get; set; } = new();
    public List<PenaltyKickDocument> Kicks { get; set; } = new();
    /// <summary>Ronda en la que quedó eliminado cada jugador (ya no puede ganar).</summary>
    public Dictionary<long, int> EliminatedInRound { get; set; } = new();
    /// <summary>El anfitrión ya ha empezado la tanda (antes se está en el intermedio de "ronda extra").</summary>
    public bool Started { get; set; }
    public bool Finished { get; set; }
    public long? WinnerId { get; set; }
    /// <summary>La tanda se cortó por falta de preguntas en la muerte súbita: el 1º se sortea entre los que seguían.</summary>
    public bool OutOfQuestions { get; set; }
    /// <summary>Preguntas del banco aún sin usar, ya barajadas (una por tiro).</summary>
    public List<QuestionSnapshot> Pool { get; set; } = new();
    /// <summary>Número de fase con el que se juegan los penaltis (la siguiente a la última de la partida).</summary>
    public int FaseNumero { get; set; }
}

public sealed class PenaltyKickDocument
{
    public long PlayerId { get; set; }
    public int Round { get; set; }
    public bool Scored { get; set; }
}

public sealed class BetDocument
{
    public long UserId { get; set; }

    /// <summary>true = apuesta a que el jugador en turno acierta.</summary>
    public bool PredictsCorrect { get; set; }
}

public sealed class QuestionSnapshot
{
    public long Id { get; set; }
    public string Enunciado { get; set; } = string.Empty;
    public string? ImagenUrl { get; set; }

    /// <summary>Dato curioso: solo viaja al anfitrión (HostQuestionInfo), nunca en el TurnStarted del grupo.</summary>
    public string? Curiosidad { get; set; }
    public int FaseNumero { get; set; } = 1;
    public string? FaseNombre { get; set; }
    public string? FaseColor { get; set; }

    /// <summary>Pregunta de pulsador: el primero en pulsar se lleva la pregunta.</summary>
    public bool EsPulsador { get; set; }

    /// <summary>Pregunta de colores: el que mejor imita un color al azar se lleva la pregunta.</summary>
    public bool EsColores { get; set; }

    /// <summary>Pregunta de ocarina: el primero en tocar la melodía se lleva la pregunta.</summary>
    public bool EsOcarina { get; set; }

    /// <summary>Pregunta de la tanda de penaltis (no cuenta como pregunta de la partida).</summary>
    public bool EsPenalti { get; set; }

    /// <summary>Pregunta sin turno (pulsador, colores u ocarina).</summary>
    public bool SinTurno => EsPulsador || EsColores || EsOcarina;
    public List<AnswerSnapshot> Respuestas { get; set; } = new();
}

public sealed class AnswerSnapshot
{
    public long Id { get; set; }
    public string Texto { get; set; } = string.Empty;
    public bool EsCorrecta { get; set; }
}

public sealed record DueDeadline(string RoomCode, long DeadlineUnixMs, long TurnGeneration);

public sealed record ConnectionMapping(long UserId, string RoomCode);
