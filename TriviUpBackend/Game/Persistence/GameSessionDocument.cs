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

    public long? GetCurrentPlayerId() => TurnQueue.Count > 0 ? TurnQueue[0] : null;

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
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public sealed class QuestionSnapshot
{
    public long Id { get; set; }
    public string Enunciado { get; set; } = string.Empty;
    public string? ImagenUrl { get; set; }
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
