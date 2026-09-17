using Microsoft.EntityFrameworkCore;
using TriviUpBackend.Database;
using TriviUpBackend.Game.Models;
using TriviUpBackend.Game.Repositories;

namespace TriviUpTest.Repositories;

public class GameHistoryRepositoryTests : IDisposable
{
    private readonly Context _context;
    private readonly GameHistoryRepository _repository;

    public GameHistoryRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<Context>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new Context(options);
        _repository = new GameHistoryRepository(_context);

        SeedDatabase();
    }

    private void SeedDatabase()
    {
        var history1 = new GameHistory
        {
            Id = 1,
            GameId = 1001,
            QuizId = 1,
            OwnerId = 1,
            QuizTitle = "Test Quiz 1",
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            EndedAt = DateTime.UtcNow,
            PlayerResultsJson = "[{\"UserId\":1,\"Username\":\"player1\",\"FinalScore\":100,\"CorrectAnswers\":5,\"WrongAnswers\":2,\"Rank\":1}]"
        };

        var history2 = new GameHistory
        {
            Id = 2,
            GameId = 1002,
            QuizId = 2,
            OwnerId = 2,
            QuizTitle = "Test Quiz 2",
            StartedAt = DateTime.UtcNow.AddMinutes(-15),
            EndedAt = DateTime.UtcNow.AddMinutes(-5),
            PlayerResultsJson = "[{\"UserId\":2,\"Username\":\"player2\",\"FinalScore\":80,\"CorrectAnswers\":4,\"WrongAnswers\":3,\"Rank\":1}]"
        };

        var history3 = new GameHistory
        {
            Id = 3,
            GameId = 1003,
            QuizId = 1,
            OwnerId = 1,
            QuizTitle = "Test Quiz 1",
            StartedAt = DateTime.UtcNow.AddMinutes(-20),
            EndedAt = DateTime.UtcNow.AddMinutes(-10),
            PlayerResultsJson = "[{\"UserId\":3,\"Username\":\"player3\",\"FinalScore\":120,\"CorrectAnswers\":6,\"WrongAnswers\":1,\"Rank\":1}]"
        };

        _context.GameHistories.AddRange(history1, history2, history3);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ========== AddAsync Tests ==========

    [Fact]
    public async Task AddAsync_ValidGameHistory_SavesGameHistory()
    {
        // Arrange
        var newHistory = new GameHistory
        {
            GameId = 2001,
            QuizId = 1,
            OwnerId = 1,
            QuizTitle = "New Game",
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            EndedAt = DateTime.UtcNow,
            PlayerResultsJson = "[]"
        };

        // Act
        await _repository.AddAsync(newHistory);

        // Assert
        var saved = await _context.GameHistories.FindAsync(newHistory.Id);
        Assert.NotNull(saved);
        Assert.Equal(2001, saved.GameId);
    }

    // ========== GetByUserIdAsync Tests ==========

    [Fact]
    public async Task GetByUserIdAsync_ExistingUserId_ReturnsGameHistories()
    {
        // Act
        var result = await _repository.GetByUserIdAsync(1);

        // Assert
        var histories = result.ToList();
        Assert.NotEmpty(histories);
        Assert.Contains(histories, h => h.OwnerId == 1);
    }

    [Fact]
    public async Task GetByUserIdAsync_ExistingUserIdInPlayerResults_ReturnsGameHistories()
    {
        // Act - User 3 is in PlayerResultsJson of history3
        var result = await _repository.GetByUserIdAsync(3);

        // Assert
        var histories = result.ToList();
        Assert.NotEmpty(histories);
    }

    [Fact]
    public async Task GetByUserIdAsync_NonExistingUserId_ReturnsEmpty()
    {
        // Act
        var result = await _repository.GetByUserIdAsync(999);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOrderedByEndedAtDescending()
    {
        // Act
        var result = await _repository.GetByUserIdAsync(1);

        // Assert
        var histories = result.ToList();
        Assert.True(histories.Count >= 2);
        for (int i = 0; i < histories.Count - 1; i++)
        {
            Assert.True(histories[i].EndedAt >= histories[i + 1].EndedAt);
        }
    }

    // ========== GetByIdAsync Tests ==========

    [Fact]
    public async Task GetByIdAsync_ExistingGameId_ReturnsGameHistory()
    {
        // Act
        var result = await _repository.GetByIdAsync(1001);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1001, result.GameId);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingGameId_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByIdAsync(9999);

        // Assert
        Assert.Null(result);
    }
}