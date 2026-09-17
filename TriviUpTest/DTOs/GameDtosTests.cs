using TriviUpBackend.Game.DTOs;

namespace TriviUpTest.DTOs;

public class GameDtosTests
{
    // ========== CreateGameRequest Tests ==========

    [Fact]
    public void CreateGameRequest_Properties_SetCorrectly()
    {
        // Arrange & Act
        var request = new CreateGameRequest(123);

        // Assert
        Assert.Equal(123, request.QuizId);
    }

    // ========== JoinGameRequest Tests ==========

    [Fact]
    public void JoinGameRequest_Properties_SetCorrectly()
    {
        // Arrange & Act
        var request = new JoinGameRequest("ABC123");

        // Assert
        Assert.Equal("ABC123", request.RoomCode);
    }

    // ========== AnswerSubmission Tests ==========

    [Fact]
    public void AnswerSubmission_Properties_SetCorrectly()
    {
        // Arrange & Act
        var submission = new AnswerSubmission("ABC123", 1, 2, 15);

        // Assert
        Assert.Equal("ABC123", submission.RoomCode);
        Assert.Equal(1, submission.QuestionId);
        Assert.Equal(2, submission.AnswerIndex);
        Assert.Equal(15, submission.TimeRemaining);
    }

    // ========== GameStateDto Tests ==========

    [Fact]
    public void GameStateDto_Properties_SetCorrectly()
    {
        // Arrange
        var players = new List<PlayerDto>
        {
            new(1, "player1", 100, 5, 2, false, true, true),
            new(2, "player2", 80, 4, 3, true, false, true)
        };

        // Act
        var dto = new GameStateDto("ABC123", "Playing", players, 1, 10);

        // Assert
        Assert.Equal("ABC123", dto.RoomCode);
        Assert.Equal("Playing", dto.State);
        Assert.Equal(2, dto.Players.Count);
        Assert.Equal(1, dto.CurrentQuestionIndex);
        Assert.Equal(10, dto.TotalQuestions);
    }

    // ========== PlayerDto Tests ==========

    [Fact]
    public void PlayerDto_Properties_SetCorrectly()
    {
        // Arrange & Act
        var player = new PlayerDto(1, "testuser", 100, 5, 2, true, false, true);

        // Assert
        Assert.Equal(1, player.UserId);
        Assert.Equal("testuser", player.Username);
        Assert.Equal(100, player.Score);
        Assert.Equal(5, player.CorrectAnswers);
        Assert.Equal(2, player.WrongAnswers);
        Assert.True(player.IsCurrentTurn);
        Assert.False(player.IsOwner);
        Assert.True(player.IsConnected);
    }

    // ========== TurnResultDto Tests ==========

    [Fact]
    public void TurnResultDto_Properties_SetCorrectly()
    {
        // Arrange & Act
        var result = new TurnResultDto(1, true, 2, 150, 250);

        // Assert
        Assert.Equal(1, result.PlayerId);
        Assert.True(result.IsCorrect);
        Assert.Equal(2, result.CorrectAnswerIndex);
        Assert.Equal(150, result.PointsEarned);
        Assert.Equal(250, result.NewTotalScore);
    }

    [Fact]
    public void TurnResultDto_IncorrectAnswer_HasZeroPoints()
    {
        // Arrange & Act
        var result = new TurnResultDto(1, false, 2, 0, 100);

        // Assert
        Assert.False(result.IsCorrect);
        Assert.Equal(0, result.PointsEarned);
    }

    // ========== GameResultDto Tests ==========

    [Fact]
    public void GameResultDto_Properties_SetCorrectly()
    {
        // Arrange
        var playerResults = new List<PlayerResultDto>
        {
            new(1, "player1", 1, 300, 10, 2, 83),
            new(2, "player2", 2, 200, 8, 4, 66)
        };
        var duration = TimeSpan.FromMinutes(5);

        // Act
        var dto = new GameResultDto("ABC123", "Test Quiz", playerResults, 10, duration);

        // Assert
        Assert.Equal("ABC123", dto.RoomCode);
        Assert.Equal("Test Quiz", dto.QuizTitle);
        Assert.Equal(2, dto.PlayerResults.Count);
        Assert.Equal(10, dto.TotalQuestions);
        Assert.Equal(duration, dto.GameDuration);
    }

    // ========== PlayerResultDto Tests ==========

    [Fact]
    public void PlayerResultDto_Properties_SetCorrectly()
    {
        // Arrange & Act
        var dto = new PlayerResultDto(1, "player1", 1, 300, 10, 2, 83);

        // Assert
        Assert.Equal(1, dto.UserId);
        Assert.Equal("player1", dto.Username);
        Assert.Equal(1, dto.Rank);
        Assert.Equal(300, dto.FinalScore);
        Assert.Equal(10, dto.CorrectAnswers);
        Assert.Equal(2, dto.WrongAnswers);
        Assert.Equal(83, dto.CorrectPercentage);
    }

    [Fact]
    public void PlayerResultDto_ZeroAnswers_HasZeroPercentage()
    {
        // Arrange & Act
        var dto = new PlayerResultDto(1, "player1", 1, 0, 0, 0, 0);

        // Assert
        Assert.Equal(0, dto.CorrectPercentage);
    }

    // ========== TurnStartedDto Tests ==========

    [Fact]
    public void TurnStartedDto_Properties_SetCorrectly()
    {
        // Arrange
        var question = new QuestionDto(1, "What is 2+2?", new List<string> { "3", "4", "5", "6" }, null);

        // Act
        var dto = new TurnStartedDto(1, true, question, 20);

        // Assert
        Assert.Equal(1, dto.CurrentPlayerId);
        Assert.True(dto.IsMyTurn);
        Assert.Equal(question, dto.Question);
        Assert.Equal(20, dto.TimeLimit);
    }

    // ========== QuestionDto Tests ==========

    [Fact]
    public void QuestionDto_Properties_SetCorrectly()
    {
        // Arrange
        var options = new List<string> { "A", "B", "C", "D" };

        // Act
        var dto = new QuestionDto(1, "What is 2+2?", options, "https://example.com/image.png");

        // Assert
        Assert.Equal(1, dto.Id);
        Assert.Equal("What is 2+2?", dto.Text);
        Assert.Equal(4, dto.Options.Count);
        Assert.Equal("https://example.com/image.png", dto.ImageUrl);
    }

    [Fact]
    public void QuestionDto_NullImageUrl_IsNull()
    {
        // Arrange & Act
        var dto = new QuestionDto(1, "Question?", new List<string> { "A", "B" }, null);

        // Assert
        Assert.Null(dto.ImageUrl);
    }

    // ========== GameHistoryDto Tests ==========

    [Fact]
    public void GameHistoryDto_Properties_SetCorrectly()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddMinutes(-10);
        var endTime = DateTime.UtcNow;
        var playerResults = new List<HistoryPlayerResultDto>
        {
            new(1, "player1", 300, 10, 2, 1)
        };

        // Act
        var dto = new GameHistoryDto(1001, 1, "Test Quiz", startTime, endTime, 1, playerResults);

        // Assert
        Assert.Equal(1001, dto.GameId);
        Assert.Equal(1, dto.QuizId);
        Assert.Equal("Test Quiz", dto.QuizTitle);
        Assert.Equal(startTime, dto.StartedAt);
        Assert.Equal(endTime, dto.EndedAt);
        Assert.Equal(1, dto.OwnerId);
        Assert.Single(dto.PlayerResults);
    }

    // ========== HistoryPlayerResultDto Tests ==========

    [Fact]
    public void HistoryPlayerResultDto_Properties_SetCorrectly()
    {
        // Arrange & Act
        var dto = new HistoryPlayerResultDto(1, "player1", 300, 10, 2, 1);

        // Assert
        Assert.Equal(1, dto.UserId);
        Assert.Equal("player1", dto.Username);
        Assert.Equal(300, dto.FinalScore);
        Assert.Equal(10, dto.CorrectAnswers);
        Assert.Equal(2, dto.WrongAnswers);
        Assert.Equal(1, dto.Rank);
    }

    // ========== GamePausedDto Tests ==========

    [Fact]
    public void GamePausedDto_Properties_SetCorrectly()
    {
        // Arrange
        var pausedAt = DateTime.UtcNow;

        // Act
        var dto = new GamePausedDto("ABC123", pausedAt);

        // Assert
        Assert.Equal("ABC123", dto.RoomCode);
        Assert.Equal(pausedAt, dto.PausedAt);
    }

    // ========== GameResumedDto Tests ==========

    [Fact]
    public void GameResumedDto_Properties_SetCorrectly()
    {
        // Arrange & Act
        var dto = new GameResumedDto("ABC123", 15);

        // Assert
        Assert.Equal("ABC123", dto.RoomCode);
        Assert.Equal(15, dto.TimeRemaining);
    }

    // ========== AdminStatsDto Tests ==========

    [Fact]
    public void AdminStatsDto_Properties_SetCorrectly()
    {
        // Arrange
        var mostFavQuiz = new QuizWithMostFavoritesDto(1, "Popular Quiz", 100);
        var mostVisQuiz = new QuizWithMostVisitsDto(2, "Visited Quiz", 500);
        var dailyGames = new List<DailyGamesDto> { new(DateTime.UtcNow, 10) };
        var activeUsers = new List<ActiveUsersDto> { new(DateTime.UtcNow, 5) };

        // Act
        var dto = new AdminStatsDto(100, 50, 25, 10, mostFavQuiz, mostVisQuiz, dailyGames, activeUsers);

        // Assert
        Assert.Equal(100, dto.TotalGamesPlayed);
        Assert.Equal(50, dto.TotalQuizzes);
        Assert.Equal(25, dto.TotalUsers);
        Assert.Equal(10, dto.ActiveUsersLast24h);
        Assert.Equal(mostFavQuiz, dto.MostFavoritesQuiz);
        Assert.Equal(mostVisQuiz, dto.MostVisitsQuiz);
        Assert.Single(dto.GamesPerDay);
        Assert.Single(dto.ActiveUsersPerDay);
    }

    [Fact]
    public void AdminStatsDto_NullQuizStats_AreNull()
    {
        // Arrange & Act
        var dto = new AdminStatsDto(0, 0, 0, 0, null, null, new List<DailyGamesDto>(), new List<ActiveUsersDto>());

        // Assert
        Assert.Null(dto.MostFavoritesQuiz);
        Assert.Null(dto.MostVisitsQuiz);
    }

    // ========== QuizWithMostFavoritesDto Tests ==========

    [Fact]
    public void QuizWithMostFavoritesDto_Properties_SetCorrectly()
    {
        // Arrange & Act
        var dto = new QuizWithMostFavoritesDto(1, "Popular Quiz", 100);

        // Assert
        Assert.Equal(1, dto.Id);
        Assert.Equal("Popular Quiz", dto.Nombre);
        Assert.Equal(100, dto.Favorites);
    }

    // ========== QuizWithMostVisitsDto Tests ==========

    [Fact]
    public void QuizWithMostVisitsDto_Properties_SetCorrectly()
    {
        // Arrange & Act
        var dto = new QuizWithMostVisitsDto(2, "Visited Quiz", 500);

        // Assert
        Assert.Equal(2, dto.Id);
        Assert.Equal("Visited Quiz", dto.Nombre);
        Assert.Equal(500, dto.Visitas);
    }

    // ========== DailyGamesDto Tests ==========

    [Fact]
    public void DailyGamesDto_Properties_SetCorrectly()
    {
        // Arrange
        var date = new DateTime(2024, 1, 15);

        // Act
        var dto = new DailyGamesDto(date, 25);

        // Assert
        Assert.Equal(date, dto.Date);
        Assert.Equal(25, dto.Count);
    }

    // ========== ActiveUsersDto Tests ==========

    [Fact]
    public void ActiveUsersDto_Properties_SetCorrectly()
    {
        // Arrange
        var date = new DateTime(2024, 1, 15);

        // Act
        var dto = new ActiveUsersDto(date, 10);

        // Assert
        Assert.Equal(date, dto.Date);
        Assert.Equal(10, dto.Count);
    }

    // ========== DTO Immutability Tests ==========

    [Fact]
    public void GameDtos_AreImmutable()
    {
        // Arrange
        var dto1 = new PlayerDto(1, "player1", 100, 5, 2, true, false, true);

        // Act - Try to modify (should not compile if truly immutable, but records allow 'with' expressions)
        var dto2 = dto1 with { Score = 200 };

        // Assert - Original unchanged
        Assert.Equal(100, dto1.Score);
        Assert.Equal(200, dto2.Score);
    }
}