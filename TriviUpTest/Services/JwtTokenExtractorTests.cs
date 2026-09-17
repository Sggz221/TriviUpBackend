using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TriviUpBackend.Services.Auth;

namespace TriviUpTest.Services;

public class JwtTokenExtractorTests
{
    private readonly JwtTokenExtractor _extractor;
    private readonly Mock<ILogger<JwtTokenExtractor>> _mockLogger;

    public JwtTokenExtractorTests()
    {
        _mockLogger = new Mock<ILogger<JwtTokenExtractor>>();
        _extractor = new JwtTokenExtractor(_mockLogger.Object);
    }

    // Helper method to create a valid JWT token
    private string CreateTestToken(long userId, string role = "USER", string email = "test@test.com")
    {
        var handler = new JwtSecurityTokenHandler();
        var key = System.Text.Encoding.UTF8.GetBytes("this-is-a-super-secret-key-that-is-at-least-32-characters-long");
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Role, role),
            new("username", "testuser")
        };

        var token = new JwtSecurityToken(
            issuer: "TriviUpApi",
            audience: "TriviUpApp",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );

        return handler.WriteToken(token);
    }

    // Helper method to create a minimal malformed token
    private string CreateMalformedToken()
    {
        return "not.a.valid.jwt.token";
    }

    // Helper method to create a token with "none" algorithm
    private string CreateNoneAlgorithmToken()
    {
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"sub\":\"123\",\"email\":\"test@test.com\"}"));
        return $"{header}.{payload}.";
    }

    // ========== ExtractUserId Tests ==========

    [Fact]
    public void ExtractUserId_ValidToken_ReturnsUserId()
    {
        // Arrange
        var token = CreateTestToken(123);

        // Act
        var result = _extractor.ExtractUserId(token);

        // Assert
        Assert.Equal(123, result);
    }

    [Fact]
    public void ExtractUserId_MalformedToken_ReturnsNull()
    {
        // Arrange
        var token = CreateMalformedToken();

        // Act
        var result = _extractor.ExtractUserId(token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractUserId_NullToken_ReturnsNull()
    {
        // Act
        var result = _extractor.ExtractUserId(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractUserId_EmptyToken_ReturnsNull()
    {
        // Act
        var result = _extractor.ExtractUserId("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractUserId_TokenWithNameIdClaim_ReturnsUserId()
    {
        // Arrange - Create token with nameid claim instead of sub
        var handler = new JwtSecurityTokenHandler();
        var key = System.Text.Encoding.UTF8.GetBytes("this-is-a-super-secret-key-that-is-at-least-32-characters-long");
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("nameid", "456"),
            new(ClaimTypes.Email, "test@test.com")
        };

        var token = new JwtSecurityToken(
            issuer: "TriviUpApi",
            audience: "TriviUpApp",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );

        // Act
        var result = _extractor.ExtractUserId(handler.WriteToken(token));

        // Assert
        Assert.Equal(456, result);
    }

    // ========== ExtractRole Tests ==========

    [Fact]
    public void ExtractRole_ValidToken_ReturnsRole()
    {
        // Arrange
        var token = CreateTestToken(1, "ADMIN");

        // Act
        var result = _extractor.ExtractRole(token);

        // Assert
        Assert.Equal("ADMIN", result);
    }

    [Fact]
    public void ExtractRole_MalformedToken_ReturnsNull()
    {
        // Arrange
        var token = CreateMalformedToken();

        // Act
        var result = _extractor.ExtractRole(token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractRole_TokenWithRoleClaim_ReturnsRole()
    {
        // Arrange
        var handler = new JwtSecurityTokenHandler();
        var key = System.Text.Encoding.UTF8.GetBytes("this-is-a-super-secret-key-that-is-at-least-32-characters-long");
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "1"),
            new("role", "USER")
        };

        var token = new JwtSecurityToken(
            issuer: "TriviUpApi",
            audience: "TriviUpApp",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );

        // Act
        var result = _extractor.ExtractRole(handler.WriteToken(token));

        // Assert
        Assert.Equal("USER", result);
    }

    // ========== IsAdmin Tests ==========

    [Fact]
    public void IsAdmin_AdminToken_ReturnsTrue()
    {
        // Arrange
        var token = CreateTestToken(1, "ADMIN");

        // Act
        var result = _extractor.IsAdmin(token);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAdmin_UserToken_ReturnsFalse()
    {
        // Arrange
        var token = CreateTestToken(1, "USER");

        // Act
        var result = _extractor.IsAdmin(token);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsAdmin_CaseInsensitive_Admin_ReturnsTrue()
    {
        // Arrange
        var handler = new JwtSecurityTokenHandler();
        var key = System.Text.Encoding.UTF8.GetBytes("this-is-a-super-secret-key-that-is-at-least-32-characters-long");
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "1"),
            new(ClaimTypes.Role, "Admin")
        };

        var token = new JwtSecurityToken(
            issuer: "TriviUpApi",
            audience: "TriviUpApp",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );

        // Act
        var result = _extractor.IsAdmin(handler.WriteToken(token));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAdmin_MalformedToken_ReturnsFalse()
    {
        // Arrange
        var token = CreateMalformedToken();

        // Act
        var result = _extractor.IsAdmin(token);

        // Assert
        Assert.False(result);
    }

    // ========== ExtractUserInfo Tests ==========

    [Fact]
    public void ExtractUserInfo_ValidToken_ReturnsUserInfo()
    {
        // Arrange
        var token = CreateTestToken(123, "ADMIN");

        // Act
        var (userId, isAdmin, role) = _extractor.ExtractUserInfo(token);

        // Assert
        Assert.Equal(123, userId);
        Assert.True(isAdmin);
        Assert.Equal("ADMIN", role);
    }

    [Fact]
    public void ExtractUserInfo_UserToken_ReturnsNotAdmin()
    {
        // Arrange
        var token = CreateTestToken(1, "USER");

        // Act
        var (userId, isAdmin, role) = _extractor.ExtractUserInfo(token);

        // Assert
        Assert.Equal(1, userId);
        Assert.False(isAdmin);
        Assert.Equal("USER", role);
    }

    [Fact]
    public void ExtractUserInfo_MalformedToken_ReturnsNulls()
    {
        // Arrange
        var token = CreateMalformedToken();

        // Act
        var (userId, isAdmin, role) = _extractor.ExtractUserInfo(token);

        // Assert
        Assert.Null(userId);
        Assert.False(isAdmin);
        Assert.Null(role);
    }

    // ========== ExtractEmail Tests ==========

    [Fact]
    public void ExtractEmail_ValidToken_ReturnsEmail()
    {
        // Arrange
        var token = CreateTestToken(1, "USER", "test@example.com");

        // Act
        var result = _extractor.ExtractEmail(token);

        // Assert
        Assert.Equal("test@example.com", result);
    }

    [Fact]
    public void ExtractEmail_MalformedToken_ReturnsNull()
    {
        // Arrange
        var token = CreateMalformedToken();

        // Act
        var result = _extractor.ExtractEmail(token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractEmail_TokenWithEmailClaim_ReturnsEmail()
    {
        // Arrange
        var handler = new JwtSecurityTokenHandler();
        var key = System.Text.Encoding.UTF8.GetBytes("this-is-a-super-secret-key-that-is-at-least-32-characters-long");
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "1"),
            new(ClaimTypes.Email, "claim@example.com")
        };

        var token = new JwtSecurityToken(
            issuer: "TriviUpApi",
            audience: "TriviUpApp",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );

        // Act
        var result = _extractor.ExtractEmail(handler.WriteToken(token));

        // Assert
        Assert.Equal("claim@example.com", result);
    }

    // ========== IsValidTokenFormat Tests ==========

    [Fact]
    public void IsValidTokenFormat_ValidToken_ReturnsTrue()
    {
        // Arrange
        var token = CreateTestToken(1);

        // Act
        var result = _extractor.IsValidTokenFormat(token);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidTokenFormat_NullToken_ReturnsFalse()
    {
        // Act
        var result = _extractor.IsValidTokenFormat(null!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidTokenFormat_EmptyToken_ReturnsFalse()
    {
        // Act
        var result = _extractor.IsValidTokenFormat("");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidTokenFormat_WhitespaceToken_ReturnsFalse()
    {
        // Act
        var result = _extractor.IsValidTokenFormat("   ");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidTokenFormat_MalformedToken_ReturnsFalse()
    {
        // Arrange
        var token = "not.valid";

        // Act
        var result = _extractor.IsValidTokenFormat(token);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidTokenFormat_OnlyTwoParts_ReturnsFalse()
    {
        // Arrange
        var token = "part1.part2";

        // Act
        var result = _extractor.IsValidTokenFormat(token);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidTokenFormat_EmptyParts_ReturnsFalse()
    {
        // Arrange
        var token = "..";

        // Act
        var result = _extractor.IsValidTokenFormat(token);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidTokenFormat_NoneAlgorithm_ReturnsTrue()
    {
        // Arrange
        var token = CreateNoneAlgorithmToken();

        // Act
        var result = _extractor.IsValidTokenFormat(token);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidTokenFormat_FourParts_ReturnsFalse()
    {
        // Arrange
        var token = "a.b.c.d";

        // Act
        var result = _extractor.IsValidTokenFormat(token);

        // Assert
        Assert.False(result);
    }

    // ========== ExtractClaims Tests ==========

    [Fact]
    public void ExtractClaims_ValidToken_ReturnsClaimsPrincipal()
    {
        // Arrange
        var token = CreateTestToken(123, "ADMIN");

        // Act
        var result = _extractor.ExtractClaims(token);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public void ExtractClaims_MalformedToken_ReturnsNull()
    {
        // Arrange
        var token = CreateMalformedToken();

        // Act
        var result = _extractor.ExtractClaims(token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractClaims_NullToken_ReturnsNull()
    {
        // Act
        var result = _extractor.ExtractClaims(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractClaims_ValidTokenWithClaims_ContainsNormalizedClaims()
    {
        // Arrange
        var token = CreateTestToken(1, "USER", "test@test.com");

        // Act
        var result = _extractor.ExtractClaims(token);

        // Assert
        Assert.NotNull(result);
        var claims = result.Claims.ToList();
        Assert.NotEmpty(claims);
        // Check that role claim is normalized
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role);
    }

    [Fact]
    public void ExtractClaims_TokenWithNoneAlgorithm_ReturnsClaimsPrincipal()
    {
        // Arrange
        var token = CreateNoneAlgorithmToken();

        // Act
        var result = _extractor.ExtractClaims(token);

        // Assert - none algorithm tokens are still parsed, just considered invalid for security
        Assert.NotNull(result);
    }
}