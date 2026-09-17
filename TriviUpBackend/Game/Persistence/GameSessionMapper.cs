using System.Text.Json;
using TriviUpBackend.Cuestionarios.Entities;
using TriviUpBackend.Game.Models;

namespace TriviUpBackend.Game.Persistence;

/// <summary>
/// Mapeo entre documentos Redis y modelos de dominio en memoria.
/// </summary>
public static class GameSessionMapper
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static GameSessionDocument FromRoom(GameRoom room, long turnGeneration = 0, long? turnDeadlineUnixMs = null)
    {
        return new GameSessionDocument
        {
            RoomCode = room.RoomCode,
            QuizId = room.QuizId,
            QuizTitle = room.QuizTitle,
            OwnerId = room.OwnerId,
            State = room.State,
            Players = room.Players.Select(ToPlayerDocument).ToList(),
            TurnQueue = room.TurnOrder.ToList(),
            CurrentQuestionIndex = room.CurrentQuestionIndex,
            Questions = room.Questions.Select(ToQuestionSnapshot).ToList(),
            CreatedAt = room.CreatedAt,
            StartedAt = room.StartedAt,
            EndedAt = room.EndedAt,
            TurnStartedAt = room.TurnStartedAt,
            PausedTimeRemaining = room.PausedTimeRemaining,
            TurnGeneration = turnGeneration,
            TurnDeadlineUnixMs = turnDeadlineUnixMs
        };
    }

    public static GameRoom ToRoom(GameSessionDocument doc)
    {
        return new GameRoom
        {
            RoomCode = doc.RoomCode,
            QuizId = doc.QuizId,
            QuizTitle = doc.QuizTitle,
            OwnerId = doc.OwnerId,
            State = doc.State,
            Players = doc.Players.Select(ToPlayer).ToList(),
            TurnOrder = new Queue<long>(doc.TurnQueue),
            CurrentQuestionIndex = doc.CurrentQuestionIndex,
            Questions = doc.Questions.Select(ToPregunta).ToList(),
            CreatedAt = doc.CreatedAt,
            StartedAt = doc.StartedAt,
            EndedAt = doc.EndedAt,
            TurnStartedAt = doc.TurnStartedAt,
            PausedTimeRemaining = doc.PausedTimeRemaining
        };
    }

    public static PlayerDocument ToPlayerDocument(Player player) => new()
    {
        UserId = player.UserId,
        Username = player.Username,
        ConnectionId = player.ConnectionId,
        Score = player.Score,
        CorrectAnswers = player.CorrectAnswers,
        WrongAnswers = player.WrongAnswers,
        TurnPosition = player.TurnPosition,
        IsConnected = player.IsConnected,
        IsOwner = player.IsOwner,
        JoinedAt = player.JoinedAt
    };

    public static Player ToPlayer(PlayerDocument doc) => new()
    {
        UserId = doc.UserId,
        Username = doc.Username,
        ConnectionId = doc.ConnectionId,
        Score = doc.Score,
        CorrectAnswers = doc.CorrectAnswers,
        WrongAnswers = doc.WrongAnswers,
        TurnPosition = doc.TurnPosition,
        IsConnected = doc.IsConnected,
        IsOwner = doc.IsOwner,
        JoinedAt = doc.JoinedAt
    };

    public static QuestionSnapshot ToQuestionSnapshot(Pregunta pregunta) => new()
    {
        Id = pregunta.Id,
        Enunciado = pregunta.Enunciado,
        ImagenUrl = pregunta.ImagenUrl,
        Respuestas = pregunta.Respuestas.Select(r => new AnswerSnapshot
        {
            Id = r.Id,
            Texto = r.Texto,
            EsCorrecta = r.EsCorrecta
        }).ToList()
    };

    public static Pregunta ToPregunta(QuestionSnapshot snapshot) => new()
    {
        Id = snapshot.Id,
        Enunciado = snapshot.Enunciado,
        ImagenUrl = snapshot.ImagenUrl,
        Respuestas = snapshot.Respuestas.Select(r => new Respuesta
        {
            Id = r.Id,
            Texto = r.Texto,
            EsCorrecta = r.EsCorrecta
        }).ToList()
    };

    public static List<QuestionSnapshot> SnapshotQuestions(IEnumerable<Pregunta> questions) =>
        questions.Select(ToQuestionSnapshot).ToList();
}
