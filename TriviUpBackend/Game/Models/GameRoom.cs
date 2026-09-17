using TriviUpBackend.Cuestionarios.Entities;

namespace TriviUpBackend.Game.Models;

/// <summary>
/// Representa una sala de juego en tiempo real.
/// Contiene el estado del juego, jugadores, preguntas y gestión de turnos.
/// </summary>
public class GameRoom
{
    public string RoomCode { get; set; } = string.Empty;
    public long QuizId { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public long OwnerId { get; set; }
    public GameState State { get; set; } = GameState.Waiting;
    public List<Player> Players { get; set; } = new();
    public Queue<long> TurnOrder { get; set; } = new();
    public int CurrentQuestionIndex { get; set; }
    public List<Pregunta> Questions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime? TurnStartedAt { get; set; }
    public int? PausedTimeRemaining { get; set; }
}
