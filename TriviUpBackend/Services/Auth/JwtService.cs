using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TriviUpBackend.Models.Auth;

namespace TriviUpBackend.Services.Auth;

/// <summary>
/// Implementación del servicio de generación y validación de tokens JWT.
/// </summary>
public class JwtService(
    IConfiguration configuration,
    ILogger<JwtService> logger
) : IJwtService
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<JwtService> _logger = logger;

    /// <inheritdoc cref="IJwtService.GenerateToken"/>
    public string GenerateToken(User user)
    {
        var key = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT Key no configurada");
        var issuer = _configuration["Jwt:Issuer"] ?? "TiendaApi";
        var audience = _configuration["Jwt:Audience"] ?? "TiendaApi";
        var expireMinutes = int.Parse(_configuration["Jwt:ExpireMinutes"] ?? "60");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("username", user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expireMinutes),
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogInformation("Token generado - UserId: {UserId}, Username: {Username}, Role: {Role}",
            user.Id, user.Username, user.Role);
        _logger.LogDebug("Claims del token: Sub={Sub}, Email={Email}, Role={Role}",
            user.Id.ToString(), user.Email, user.Role);

        return tokenString;
    }
    
    /// <inheritdoc cref="IJwtService.ValidateToken"/>
    public string? ValidateToken(string token)
    {
        try
        {
            var key = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT Key no configurada");
            var issuer = _configuration["Jwt:Issuer"] ?? "TiendaApi";
            var audience = _configuration["Jwt:Audience"] ?? "TiendaApi";

            var tokenHandler = new JwtSecurityTokenHandler();
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = securityKey,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            var username = jwtToken.Claims.First(x => x.Type == JwtRegisteredClaimNames.Sub).Value;

            return username;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Validación de token JWT fallida");
            return null;
        }
    }
}