namespace TriviUpBackend.Game.Models;

/// <summary>
/// Representa un jugador dentro de una sala de juego.
/// </summary>
public class Player
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public int Score { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int TurnPosition { get; set; } = -1;
    public bool IsConnected { get; set; } = true;
    public bool IsOwner { get; set; } = false;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
