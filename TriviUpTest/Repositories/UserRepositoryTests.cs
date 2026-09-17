using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TriviUpBackend.Database;
using TriviUpBackend.Models.Auth;
using TriviUpBackend.Repositories.Users;

namespace TriviUpTest.Repositories;

public class UserRepositoryTests : IDisposable
{
    private readonly Context _context;
    private readonly UserRepository _repository;
    private readonly Mock<ILogger<UserRepository>> _mockLogger;

    public UserRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<Context>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new Context(options);
        _mockLogger = new Mock<ILogger<UserRepository>>();
        _repository = new UserRepository(_context, _mockLogger.Object);

        SeedDatabase();
    }

    private void SeedDatabase()
    {
        var user1 = new User
        {
            Id = 1,
            Username = "testuser",
            Email = "test@test.com",
            PasswordHash = "hash123",
            Role = UserRoles.USER,
            IsDeleted = false,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var user2 = new User
        {
            Id = 2,
            Username = "admin",
            Email = "admin@test.com",
            PasswordHash = "hash456",
            Role = UserRoles.ADMIN,
            IsDeleted = false,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var user3 = new User
        {
            Id = 3,
            Username = "deleteduser",
            Email = "deleted@test.com",
            PasswordHash = "hash789",
            Role = UserRoles.USER,
            IsDeleted = true,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Users.AddRange(user1, user2, user3);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ========== FindByIdAsync Tests ==========

    [Fact]
    public async Task FindByIdAsync_ExistingId_ReturnsUser()
    {
        // Act
        var result = await _repository.FindByIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("testuser", result.Username);
    }

    [Fact]
    public async Task FindByIdAsync_NonExistingId_ReturnsNull()
    {
        // Act
        var result = await _repository.FindByIdAsync(999);

        // Assert
        Assert.Null(result);
    }

    // ========== FindByUsernameAsync Tests ==========

    [Fact]
    public async Task FindByUsernameAsync_ExistingUsername_ReturnsUser()
    {
        // Act
        var result = await _repository.FindByUsernameAsync("testuser");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("testuser", result.Username);
    }

    [Fact]
    public async Task FindByUsernameAsync_NonExistingUsername_ReturnsNull()
    {
        // Act
        var result = await _repository.FindByUsernameAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    // ========== FindByEmailAsync Tests ==========

    [Fact]
    public async Task FindByEmailAsync_ExistingEmail_ReturnsUser()
    {
        // Act
        var result = await _repository.FindByEmailAsync("test@test.com");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test@test.com", result.Email);
    }

    [Fact]
    public async Task FindByEmailAsync_NonExistingEmail_ReturnsNull()
    {
        // Act
        var result = await _repository.FindByEmailAsync("nonexistent@test.com");

        // Assert
        Assert.Null(result);
    }

    // ========== FindByGoogleIdAsync Tests ==========

    [Fact]
    public async Task FindByGoogleIdAsync_ExistingGoogleId_ReturnsUser()
    {
        // Arrange - Add a user with GoogleId
        var userWithGoogle = new User
        {
            Id = 100,
            Username = "googleuser",
            Email = "google@test.com",
            PasswordHash = "hash",
            GoogleId = "google-123",
            Role = UserRoles.USER,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Users.Add(userWithGoogle);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.FindByGoogleIdAsync("google-123");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("google-123", result.GoogleId);
    }

    [Fact]
    public async Task FindByGoogleIdAsync_NonExistingGoogleId_ReturnsNull()
    {
        // Act
        var result = await _repository.FindByGoogleIdAsync("nonexistent-google-id");

        // Assert
        Assert.Null(result);
    }

    // ========== FindAllAsync Tests ==========

    [Fact]
    public async Task FindAllAsync_ReturnsAllUsersOrderedById()
    {
        // Act
        var result = await _repository.FindAllAsync();

        // Assert - Note: EF Core's global query filter for IsDeleted is applied
        // so only non-deleted users are returned
        var users = result.ToList();
        Assert.Equal(2, users.Count);
        Assert.Equal(1, users[0].Id);
        Assert.Equal(2, users[1].Id);
    }

    // ========== SaveAsync Tests ==========

    [Fact]
    public async Task SaveAsync_NewUser_SavesAndReturnsUser()
    {
        // Arrange
        var newUser = new User
        {
            Username = "newuser",
            Email = "new@test.com",
            PasswordHash = "newhash",
            Role = UserRoles.USER,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _repository.SaveAsync(newUser);

        // Assert
        Assert.True(result.Id > 0);
        Assert.Equal("newuser", result.Username);

        // Verify it's in the database
        var saved = await _context.Users.FindAsync(result.Id);
        Assert.NotNull(saved);
        Assert.Equal("new@test.com", saved.Email);
    }

    // ========== UpdateAsync Tests ==========

    [Fact]
    public async Task UpdateAsync_ExistingUser_UpdatesUser()
    {
        // Arrange
        var user = await _repository.FindByIdAsync(1);
        user!.Username = "updateduser";

        // Act
        var result = await _repository.UpdateAsync(user);

        // Assert
        Assert.Equal("updateduser", result.Username);

        // Verify it's updated in the database
        var updated = await _context.Users.FindAsync(1L);
        Assert.Equal("updateduser", updated!.Username);
    }

    // ========== DeleteAsync Tests ==========

    [Fact]
    public async Task DeleteAsync_ExistingId_SoftDeletesUser()
    {
        // Act
        await _repository.DeleteAsync(1);

        // Assert - User should still exist but be marked as deleted
        var user = await _context.Users.FindAsync(1L);
        Assert.NotNull(user);
        Assert.True(user.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingId_DoesNotThrow()
    {
        // Act & Assert - should not throw
        await _repository.DeleteAsync(999);
    }

    // ========== GetActiveUsersAsync Tests ==========

    [Fact]
    public async Task GetActiveUsersAsync_ReturnsOnlyNonDeletedUsers()
    {
        // Act
        var result = await _repository.GetActiveUsersAsync();

        // Assert - Should only return non-deleted users (user 1 and 2, not user 3)
        var users = result.ToList();
        Assert.Equal(2, users.Count);
        Assert.DoesNotContain(users, u => u.Username == "deleteduser");
    }

    [Fact]
    public async Task GetActiveUsersAsync_ReturnsUsersOrderedByEmail()
    {
        // Act
        var result = await _repository.GetActiveUsersAsync();

        // Assert
        var users = result.ToList();
        Assert.Equal(2, users.Count);
        // admin@test.com comes before test@test.com alphabetically
        Assert.Equal("admin", users[0].Username);
        Assert.Equal("testuser", users[1].Username);
    }
}