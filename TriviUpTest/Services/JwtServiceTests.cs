using Xunit;
using Moq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using TriviUpBackend.Services.Auth;
using TriviUpBackend.Models.Auth;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TriviUpTest.Services;

public class JwtServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<JwtService>> _mockLogger;
    private readonly JwtService _service;

    public JwtServiceTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<JwtService>>();

        var jwtSection = new MockConfigurationSection();
        jwtSection["Key"] = "this-is-a-super-secret-key-that-is-at-least-32-characters-long";
        jwtSection["Issuer"] = "TriviUpApi";
        jwtSection["Audience"] = "TriviUpApp";
        jwtSection["ExpireMinutes"] = "60";

        // Setup root indexer to delegate to sections properly
        _mockConfiguration.Setup(c => c["Jwt:Key"]).Returns(jwtSection["Key"]);
        _mockConfiguration.Setup(c => c["Jwt:Issuer"]).Returns(jwtSection["Issuer"]);
        _mockConfiguration.Setup(c => c["Jwt:Audience"]).Returns(jwtSection["Audience"]);
        _mockConfiguration.Setup(c => c["Jwt:ExpireMinutes"]).Returns(jwtSection["ExpireMinutes"]);
        _mockConfiguration.Setup(c => c.GetSection("Jwt")).Returns(jwtSection);

        _service = new JwtService(_mockConfiguration.Object, _mockLogger.Object);
    }

    // ========== GenerateToken Tests ==========

    [Fact]
    public void GenerateToken_ValidUser_ReturnsValidJwtString()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            Email = "test@example.com",
            Role = UserRoles.USER
        };

        // Act
        var token = _service.GenerateToken(user);

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
        Assert.IsType<string>(token);
    }

    [Fact]
    public void GenerateToken_ValidUser_TokenContainsExpectedClaims()
    {
        // Arrange
        var user = new User
        {
            Id = 42,
            Username = "testuser",
            Email = "test@example.com",
            Role = UserRoles.ADMIN
        };

        // Act
        var token = _service.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        Assert.Equal("42", jwtToken.Subject);
        Assert.Equal("test@example.com", jwtToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(UserRoles.ADMIN, jwtToken.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal("testuser", jwtToken.Claims.First(c => c.Type == "username").Value);
    }

    [Fact]
    public void GenerateToken_ValidUser_TokenHasCorrectExpiration()
    {
        // Arrange
        var user = new User { Id = 1, Username = "test", Email = "test@test.com", Role = UserRoles.USER };
        var beforeGeneration = DateTime.UtcNow;

        // Act
        var token = _service.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        var expectedExpiration = beforeGeneration.AddMinutes(60);
        Assert.True(jwtToken.ValidTo >= beforeGeneration.AddMinutes(59));
        Assert.True(jwtToken.ValidTo <= expectedExpiration.AddMinutes(1));
    }

    [Fact]
    public void GenerateToken_ValidUser_TokenContainsJtiClaim()
    {
        // Arrange
        var user = new User { Id = 1, Username = "test", Email = "test@test.com", Role = UserRoles.USER };

        // Act
        var token = _service.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        var jtiClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti);
        Assert.NotNull(jtiClaim);
        Assert.NotEmpty(jtiClaim.Value);
    }

    [Fact]
    public void GenerateToken_MissingJwtKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var emptyConfig = new Mock<IConfiguration>();
        emptyConfig.Setup(c => c["Jwt"]).Returns((string?)null);
        emptyConfig.Setup(c => c.GetSection("Jwt")).Returns(new MockConfigurationSection());

        var service = new JwtService(emptyConfig.Object, _mockLogger.Object);
        var user = new User { Id = 1, Username = "test", Email = "test@test.com", Role = UserRoles.USER };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => service.GenerateToken(user));
    }

    [Fact]
    public void GenerateToken_ValidUser_IssuerAndAudienceAreCorrect()
    {
        // Arrange
        var user = new User { Id = 1, Username = "test", Email = "test@test.com", Role = UserRoles.USER };

        // Act
        var token = _service.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        // Assert
        Assert.Equal("TriviUpApi", jwtToken.Issuer);
        Assert.Contains("TriviUpApp", jwtToken.Audiences);
    }

    // ========== ValidateToken Tests ==========

    [Fact]
    public void ValidateToken_ValidToken_ReturnsUserId()
    {
        // Arrange
        var user = new User
        {
            Id = 123,
            Username = "testuser",
            Email = "test@example.com",
            Role = UserRoles.USER
        };
        var token = _service.GenerateToken(user);

        // Act
        var result = _service.ValidateToken(token);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("123", result);
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsNull()
    {
        // Arrange
        var invalidToken = "this.is.not.a.valid.token";

        // Act
        var result = _service.ValidateToken(invalidToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_TamperedToken_ReturnsNull()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            Email = "test@example.com",
            Role = UserRoles.USER
        };
        var token = _service.GenerateToken(user);
        var tamperedToken = token + "tampered";

        // Act
        var result = _service.ValidateToken(tamperedToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_ExpiredToken_ReturnsNull()
    {
        // Arrange - Create a service with very short expiration
        var shortLivedConfig = new Mock<IConfiguration>();
        var jwtSection = new MockConfigurationSection();
        jwtSection["Key"] = "this-is-a-super-secret-key-that-is-at-least-32-characters-long";
        jwtSection["Issuer"] = "TriviUpApi";
        jwtSection["Audience"] = "TriviUpApp";
        jwtSection["ExpireMinutes"] = "-1"; // Already expired

        shortLivedConfig.Setup(c => c["Jwt:Key"]).Returns(jwtSection["Key"]);
        shortLivedConfig.Setup(c => c["Jwt:Issuer"]).Returns(jwtSection["Issuer"]);
        shortLivedConfig.Setup(c => c["Jwt:Audience"]).Returns(jwtSection["Audience"]);
        shortLivedConfig.Setup(c => c["Jwt:ExpireMinutes"]).Returns(jwtSection["ExpireMinutes"]);
        shortLivedConfig.Setup(c => c.GetSection("Jwt")).Returns(jwtSection);

        var shortLivedService = new JwtService(shortLivedConfig.Object, _mockLogger.Object);
        var user = new User { Id = 1, Username = "test", Email = "test@test.com", Role = UserRoles.USER };
        var token = shortLivedService.GenerateToken(user);

        // Act
        var result = _service.ValidateToken(token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_WrongKey_ReturnsNull()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            Email = "test@example.com",
            Role = UserRoles.USER
        };
        var token = _service.GenerateToken(user);

        // Create a service with a different key
        var differentKeyConfig = new Mock<IConfiguration>();
        var jwtSection = new MockConfigurationSection();
        jwtSection["Key"] = "completely-different-secret-key-that-is-also-32-chars-min";
        jwtSection["Issuer"] = "TriviUpApi";
        jwtSection["Audience"] = "TriviUpApp";
        jwtSection["ExpireMinutes"] = "60";

        differentKeyConfig.Setup(c => c["Jwt:Key"]).Returns(jwtSection["Key"]);
        differentKeyConfig.Setup(c => c["Jwt:Issuer"]).Returns(jwtSection["Issuer"]);
        differentKeyConfig.Setup(c => c["Jwt:Audience"]).Returns(jwtSection["Audience"]);
        differentKeyConfig.Setup(c => c["Jwt:ExpireMinutes"]).Returns(jwtSection["ExpireMinutes"]);
        differentKeyConfig.Setup(c => c.GetSection("Jwt")).Returns(jwtSection);

        var differentKeyService = new JwtService(differentKeyConfig.Object, _mockLogger.Object);

        // Act
        var result = differentKeyService.ValidateToken(token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_WrongIssuer_ReturnsNull()
    {
        // Arrange - Generate token with correct issuer
        var user = new User { Id = 1, Username = "testuser", Email = "test@example.com", Role = UserRoles.USER };
        var token = _service.GenerateToken(user);

        // Create a service with a different issuer
        var differentIssuerConfig = new Mock<IConfiguration>();
        var jwtSection = new MockConfigurationSection();
        jwtSection["Key"] = "this-is-a-super-secret-key-that-is-at-least-32-characters-long";
        jwtSection["Issuer"] = "DifferentIssuer";
        jwtSection["Audience"] = "TriviUpApp";
        jwtSection["ExpireMinutes"] = "60";

        differentIssuerConfig.Setup(c => c["Jwt:Key"]).Returns(jwtSection["Key"]);
        differentIssuerConfig.Setup(c => c["Jwt:Issuer"]).Returns(jwtSection["Issuer"]);
        differentIssuerConfig.Setup(c => c["Jwt:Audience"]).Returns(jwtSection["Audience"]);
        differentIssuerConfig.Setup(c => c["Jwt:ExpireMinutes"]).Returns(jwtSection["ExpireMinutes"]);
        differentIssuerConfig.Setup(c => c.GetSection("Jwt")).Returns(jwtSection);

        var differentIssuerService = new JwtService(differentIssuerConfig.Object, _mockLogger.Object);

        // Act
        var result = differentIssuerService.ValidateToken(token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_WrongAudience_ReturnsNull()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Username = "testuser",
            Email = "test@example.com",
            Role = UserRoles.USER
        };
        var token = _service.GenerateToken(user);

        // Create a service with a different audience
        var differentAudienceConfig = new Mock<IConfiguration>();
        var jwtSection = new MockConfigurationSection();
        jwtSection["Key"] = "this-is-a-super-secret-key-that-is-at-least-32-characters-long";
        jwtSection["Issuer"] = "TriviUpApi";
        jwtSection["Audience"] = "DifferentAudience";
        jwtSection["ExpireMinutes"] = "60";

        differentAudienceConfig.Setup(c => c["Jwt:Key"]).Returns(jwtSection["Key"]);
        differentAudienceConfig.Setup(c => c["Jwt:Issuer"]).Returns(jwtSection["Issuer"]);
        differentAudienceConfig.Setup(c => c["Jwt:Audience"]).Returns(jwtSection["Audience"]);
        differentAudienceConfig.Setup(c => c["Jwt:ExpireMinutes"]).Returns(jwtSection["ExpireMinutes"]);
        differentAudienceConfig.Setup(c => c.GetSection("Jwt")).Returns(jwtSection);

        var differentAudienceService = new JwtService(differentAudienceConfig.Object, _mockLogger.Object);

        // Act
        var result = differentAudienceService.ValidateToken(token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_MissingJwtKeyInConfig_ReturnsNull()
    {
        // Arrange
        var user = new User { Id = 1, Username = "test", Email = "test@test.com", Role = UserRoles.USER };
        var token = _service.GenerateToken(user);

        var emptyConfig = new Mock<IConfiguration>();
        emptyConfig.Setup(c => c["Jwt:Key"]).Returns((string?)null);
        emptyConfig.Setup(c => c["Jwt:Issuer"]).Returns("TriviUpApi");
        emptyConfig.Setup(c => c["Jwt:Audience"]).Returns("TriviUpApp");
        emptyConfig.Setup(c => c["Jwt:ExpireMinutes"]).Returns("60");
        emptyConfig.Setup(c => c.GetSection("Jwt")).Returns(new MockConfigurationSection());

        var service = new JwtService(emptyConfig.Object, _mockLogger.Object);

        // Act
        var result = service.ValidateToken(token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_EmptyToken_ReturnsNull()
    {
        // Act
        var result = _service.ValidateToken(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_NullToken_ReturnsNull()
    {
        // Act
        var result = _service.ValidateToken(null!);

        // Assert
        Assert.Null(result);
    }

    // ========== Helper Classes ==========

    private class MockConfigurationSection : IConfigurationSection
    {
        private readonly Dictionary<string, string?> _values = new();

        public string Path => "Jwt";
        public string Key => "Jwt";

        public string? Value
        {
            get => _values.TryGetValue("", out var v) ? v : null;
            set => _values[""] = value;
        }

        public string this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v ?? "" : "";
            set => _values[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();
        public IChangeToken GetReloadToken() => null!;
        public bool Exists() => true;
        public IConfigurationSection GetSection(string key) => throw new NotImplementedException();
    }
}
