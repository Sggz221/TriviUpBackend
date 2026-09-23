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

    /// <summary>Índices de respuestas eliminadas por la ruleta en la pregunta actual.</summary>
    public List<int> EliminatedAnswerIndexes { get; set; } = new();

    /// <summary>Jugadores con "Doble o nada" activo en la pregunta actual.</summary>
    public List<long> DoubleOrNothingPlayers { get; set; } = new();

    /// <summary>Apuestas sobre el jugador en turno en la pregunta actual.</summary>
    public List<BetDocument> Bets { get; set; } = new();

    public long? GetCurrentPlayerId() => TurnQueue.Count > 0 ? TurnQueue[0] : null;

    /// <summary>Quien responde ahora: el ladrón durante un robo o, si no, el jugador en turno.</summary>
    public long? GetAnsweringPlayerId() => StealActive && StolenById.HasValue ? StolenById : GetCurrentPlayerId();

    public void ResetQuestionState()
    {
        StolenById = null;
        StealActive = false;
        EliminatedAnswerIndexes = new();
        DoubleOrNothingPlayers = new();
        Bets = new();
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

    /// <summary>Comodines ya gastados (cada jugador tiene uno de cada, no recuperables).</summary>
    public List<ComodinTipo> UsedComodines { get; set; } = new();

    public bool CanPlay() => !IsOwner && !IsSpectator;

    public List<ComodinTipo> AvailableComodines() =>
        CanPlay() ? ComodinReglas.Todos.Where(c => !UsedComodines.Contains(c)).ToList() : new();
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
    public int FaseNumero { get; set; } = 1;
    public string? FaseNombre { get; set; }
    public string? FaseColor { get; set; }
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
