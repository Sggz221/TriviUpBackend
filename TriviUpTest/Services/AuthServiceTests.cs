using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using TriviUpBackend.Services.Auth;
using TriviUpBackend.DTO.User;
using TriviUpBackend.Models.Auth;
using TriviUpBackend.Repositories.Users;
using TriviUpBackend.Errors;
using TriviUpBackend.Common.Storage;

namespace TriviUpTest.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly Mock<IJwtService> _mockJwtService;
    private readonly Mock<IProfilePhotoStorage> _mockProfilePhotoStorage;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _mockUserRepo = new Mock<IUserRepository>();
        _mockJwtService = new Mock<IJwtService>();
        _mockProfilePhotoStorage = new Mock<IProfilePhotoStorage>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _service = new AuthService(_mockUserRepo.Object, _mockJwtService.Object, _mockProfilePhotoStorage.Object, _mockLogger.Object);
    }

    // ========== SignUpAsync Tests ==========

    [Fact]
    public async Task SignUpAsync_ValidRequest_ReturnsSuccessWithAuthResponse()
    {
        // Arrange
        var dto = new RegisterDto
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = "password123"
        };

        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.FindByEmailAsync(dto.Email)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.SaveAsync(It.IsAny<User>())).ReturnsAsync((User u) =>
        {
            u.Id = 1L;
            return u;
        });
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("mock-jwt-token");

        // Act
        var result = await _service.SignUpAsync(dto);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("mock-jwt-token", result.Value.Token);
        Assert.Equal("testuser", result.Value.User.Username);
    }

    [Fact]
    public async Task SignUpAsync_DuplicateUsername_ReturnsConflictError()
    {
        // Arrange
        var dto = new RegisterDto
        {
            Username = "existinguser",
            Email = "test@example.com",
            Password = "password123"
        };

        var existingUser = new User { Id = 1, Username = "existinguser", Email = "other@example.com" };
        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync(existingUser);

        // Act
        var result = await _service.SignUpAsync(dto);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<AuthConflictError>(result.Error);
    }

    [Fact]
    public async Task SignUpAsync_DuplicateEmail_ReturnsConflictError()
    {
        // Arrange
        var dto = new RegisterDto
        {
            Username = "newuser",
            Email = "existing@example.com",
            Password = "password123"
        };

        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.FindByEmailAsync(dto.Email)).ReturnsAsync(new User { Id = 1, Username = "other", Email = dto.Email });

        // Act
        var result = await _service.SignUpAsync(dto);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<AuthConflictError>(result.Error);
    }

    [Fact]
    public async Task SignUpAsync_UsernameWithNewlines_IsSanitized()
    {
        // Arrange
        var dto = new RegisterDto
        {
            Username = "test\u000Auser", // Contains newline
            Email = "test@example.com",
            Password = "password123"
        };

        _mockUserRepo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.FindByEmailAsync(dto.Email)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.SaveAsync(It.IsAny<User>())).ReturnsAsync((User u) =>
        {
            u.Id = 1L;
            return u;
        });
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("mock-jwt-token");

        // Act
        var result = await _service.SignUpAsync(dto);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("\n", result.Value.User.Username);
    }

    [Fact]
    public async Task SignUpAsync_RepositoryThrowsOnSave_ReturnsFailure()
    {
        // Arrange
        var dto = new RegisterDto
        {
            Username = "newuser",
            Email = "new@example.com",
            Password = "password123"
        };

        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.FindByEmailAsync(dto.Email)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.SaveAsync(It.IsAny<User>())).ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _service.SignUpAsync(dto);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SignUpAsync_RepositoryThrowsOnUpdate_ReturnsFailure()
    {
        // Arrange
        var dto = new RegisterDto
        {
            Username = "newuser",
            Email = "new@example.com",
            Password = "password123"
        };

        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.FindByEmailAsync(dto.Email)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.SaveAsync(It.IsAny<User>())).ReturnsAsync((User u) =>
        {
            u.Id = 1L;
            return u;
        });
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _service.SignUpAsync(dto);

        // Assert
        Assert.True(result.IsFailure);
    }

    // ========== SignInAsync Tests ==========

    [Fact]
    public async Task SignInAsync_ValidCredentials_ReturnsSuccessWithAuthResponse()
    {
        // Arrange
        var dto = new LoginDto { Username = "testuser", Password = "password123" };
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            Role = UserRoles.USER
        };

        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync(user);
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("mock-jwt-token");

        // Act
        var result = await _service.SignInAsync(dto);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("mock-jwt-token", result.Value.Token);
    }

    [Fact]
    public async Task SignInAsync_NonExistingUser_ReturnsUnauthorizedError()
    {
        // Arrange
        var dto = new LoginDto { Username = "nonexistent", Password = "password123" };
        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync((User?)null);

        // Act
        var result = await _service.SignInAsync(dto);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<AuthUnauthorizedError>(result.Error);
    }

    [Fact]
    public async Task SignInAsync_InvalidPassword_ReturnsUnauthorizedError()
    {
        // Arrange
        var dto = new LoginDto { Username = "testuser", Password = "wrongpassword" };
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correctpassword"),
            Role = UserRoles.USER
        };
        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync(user);

        // Act
        var result = await _service.SignInAsync(dto);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<AuthUnauthorizedError>(result.Error);
    }

    [Fact]
    public async Task SignInAsync_BannedUser_ReturnsUnauthorizedError()
    {
        // Arrange
        var dto = new LoginDto { Username = "banneduser", Password = "password123" };
        var user = new User
        {
            Id = 1,
            Username = "banneduser",
            Email = "banned@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            Role = UserRoles.USER,
            IsBanned = true
        };
        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync(user);

        // Act
        var result = await _service.SignInAsync(dto);

        // Assert
        Assert.True(result.IsFailure);
        Assert.IsType<AuthUnauthorizedError>(result.Error);
    }

    [Fact]
    public async Task SignInAsync_ValidCredentials_UpdatesLastLoginAt()
    {
        // Arrange
        var dto = new LoginDto { Username = "testuser", Password = "password123" };
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            Role = UserRoles.USER,
            LastLoginAt = DateTime.UtcNow.AddDays(-1)
        };

        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync(user);
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("mock-jwt-token");

        // Act
        var result = await _service.SignInAsync(dto);

        // Assert
        Assert.True(result.IsSuccess);
        _mockUserRepo.Verify(r => r.UpdateAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task SignInAsync_RepositoryThrows_ReturnsFailure()
    {
        // Arrange
        var dto = new LoginDto { Username = "testuser", Password = "password123" };
        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _service.SignInAsync(dto);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SignInAsync_UsernameWithNewlines_IsSanitized()
    {
        // Arrange
        var dto = new LoginDto { Username = "test\u000Auser", Password = "password123" };
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            Role = UserRoles.USER
        };

        _mockUserRepo.Setup(r => r.FindByUsernameAsync(dto.Username)).ReturnsAsync(user);
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("mock-jwt-token");

        // Act
        var result = await _service.SignInAsync(dto);

        // Assert
        Assert.True(result.IsSuccess);
    }

    // ========== GoogleSignInAsync Tests ==========

    [Fact]
    public async Task GoogleSignInAsync_NewGoogleUser_CreatesUserAndReturnsToken()
    {
        // Arrange
        var googleId = "google-123";
        var email = "google@example.com";
        var username = "googleuser";

        _mockUserRepo.Setup(r => r.FindByGoogleIdAsync(googleId)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.FindByEmailAsync(email)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.SaveAsync(It.IsAny<User>())).ReturnsAsync((User u) =>
        {
            u.Id = 1L;
            return u;
        });
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("google-jwt-token");

        // Act
        var result = await _service.GoogleSignInAsync(googleId, email, username);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("google-jwt-token", result.Value.Token);
    }

    [Fact]
    public async Task GoogleSignInAsync_ExistingGoogleUser_ReturnsTokenWithExistingUser()
    {
        // Arrange
        var googleId = "google-123";
        var email = "existing@example.com";
        var username = "existinguser";

        var existingUser = new User
        {
            Id = 1,
            Username = "existinguser",
            Email = email,
            GoogleId = googleId,
            Role = UserRoles.USER
        };

        _mockUserRepo.Setup(r => r.FindByGoogleIdAsync(googleId)).ReturnsAsync(existingUser);
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("google-jwt-token");

        // Act
        var result = await _service.GoogleSignInAsync(googleId, email, username);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("google-jwt-token", result.Value.Token);
    }

    [Fact]
    public async Task GoogleSignInAsync_ExistingEmail_LinksGoogleIdAndReturnsToken()
    {
        // Arrange
        var googleId = "google-123";
        var email = "linked@example.com";
        var username = "newusername";

        var existingUser = new User
        {
            Id = 1,
            Username = "oldusername",
            Email = email,
            GoogleId = null,
            Role = UserRoles.USER
        };

        _mockUserRepo.Setup(r => r.FindByGoogleIdAsync(googleId)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.FindByEmailAsync(email)).ReturnsAsync(existingUser);
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("google-jwt-token");

        // Act
        var result = await _service.GoogleSignInAsync(googleId, email, username);

        // Assert
        Assert.True(result.IsSuccess);
        _mockUserRepo.Verify(r => r.UpdateAsync(It.Is<User>(u => u.GoogleId == googleId)), Times.Once);
    }

    [Fact]
    public async Task GoogleSignInAsync_ExistingGoogleUser_UpdatesLastLoginAt()
    {
        // Arrange
        var googleId = "google-123";
        var email = "existing@example.com";
        var username = "existinguser";
        var existingUser = new User
        {
            Id = 1,
            Username = "existinguser",
            Email = email,
            GoogleId = googleId,
            Role = UserRoles.USER,
            LastLoginAt = DateTime.UtcNow.AddDays(-1)
        };

        _mockUserRepo.Setup(r => r.FindByGoogleIdAsync(googleId)).ReturnsAsync(existingUser);
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("google-jwt-token");

        // Act
        var result = await _service.GoogleSignInAsync(googleId, email, username);

        // Assert
        Assert.True(result.IsSuccess);
        _mockUserRepo.Verify(r => r.UpdateAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task GoogleSignInAsync_ExistingEmail_UpdatesLastLoginAt()
    {
        // Arrange
        var googleId = "google-123";
        var email = "linked@example.com";
        var username = "newusername";
        var existingUser = new User
        {
            Id = 1,
            Username = "oldusername",
            Email = email,
            GoogleId = null,
            Role = UserRoles.USER,
            LastLoginAt = DateTime.UtcNow.AddDays(-1)
        };

        _mockUserRepo.Setup(r => r.FindByGoogleIdAsync(googleId)).ReturnsAsync((User?)null);
        _mockUserRepo.Setup(r => r.FindByEmailAsync(email)).ReturnsAsync(existingUser);
        _mockUserRepo.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _mockJwtService.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("google-jwt-token");

        // Act
        var result = await _service.GoogleSignInAsync(googleId, email, username);

        // Assert
        Assert.True(result.IsSuccess);
        _mockUserRepo.Verify(r => r.UpdateAsync(It.IsAny<User>()), Times.Once);
    }
}
