namespace TriviUpBackend.Game.Models;

/// <summary>
/// Historial de una partida jugado.
/// Se persiste en base de datos al finalizar una partida.
/// </summary>
public class GameHistory
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public long QuizId { get; set; }
    public long OwnerId { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public string PlayerResultsJson { get; set; } = "[]";

    /// <summary>
    /// Propiedad calculada que deserializa los resultados de jugadores desde JSON.
    /// </summary>
    public List<PlayerResult> PlayerResults =>
        string.IsNullOrEmpty(PlayerResultsJson)
            ? new List<PlayerResult>()
            : System.Text.Json.JsonSerializer.Deserialize<List<PlayerResult>>(PlayerResultsJson) ?? new List<PlayerResult>();
}

/// <summary>
/// Resultado individual de un jugador al finalizar la partida.
/// </summary>
public class PlayerResult
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public int FinalScore { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int Rank { get; set; }
}
