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
    public bool IsSpectator { get; set; } = false;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public List<ComodinTipo> UsedComodines { get; set; } = new();

    public List<string> AvailableComodines(GameMode mode, IReadOnlyDictionary<ComodinTipo, int>? config = null) =>
        IsOwner || IsSpectator
            ? new()
            : ComodinReglas.UsosRestantes(mode, config, UsedComodines).Keys.Select(c => c.ToString()).ToList();

    /// <summary>Usos restantes por comodín (solo los que aún puede usar).</summary>
    public Dictionary<string, int> RemainingUses(GameMode mode, IReadOnlyDictionary<ComodinTipo, int>? config = null) =>
        IsOwner || IsSpectator
            ? new()
            : ComodinReglas.UsosRestantes(mode, config, UsedComodines).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
}
