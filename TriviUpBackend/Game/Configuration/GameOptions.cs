namespace TriviUpBackend.Game.Configuration;

/// <summary>
/// Opciones de configuración para el servicio de juego.
/// </summary>
public class GameOptions
{
    public const string SectionName = "Game";

    public int MaxPlayersPerRoom { get; set; } = 10;
    public int MinPlayersToStart { get; set; } = 2;
    public int QuestionTimeLimit { get; set; } = 21;        // segundos (1 extra para gracia visual)
    public int TurnTransitionDelay { get; set; } = 2;        // segundos
    public int CountdownSeconds { get; set; } = 3;
    public int DisconnectTimeout { get; set; } = 30;        // segundos
    public int BasePoints { get; set; } = 100;
    public int TimeBonusMultiplier { get; set; } = 10;
    public int MaxTimeBonus { get; set; } = 200;

    /// <summary>TTL de salas finalizadas en Redis.</summary>
    public int FinishedRoomTtlHours { get; set; } = 24;

    /// <summary>Timeout del lock distribuido por sala (ms).</summary>
    public int RoomLockTimeoutMs { get; set; } = 5000;

    /// <summary>Intervalo del worker de deadlines (ms).</summary>
    public int DeadlinePollIntervalMs { get; set; } = 250;
}
