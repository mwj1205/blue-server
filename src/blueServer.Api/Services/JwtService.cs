using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using blueServer.Domain.Entities;
using blueServer.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace blueServer.Api.Services;

public class JwtService
{
    private readonly JwtOptions _options;

    public JwtService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateToken(Player player)
    {
        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                player.Id.ToString()),

            new Claim(
                ClaimTypes.Name,
                player.Nickname)
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.Key));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.EffectiveAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(_options.AccessTokenDays),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];

        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        return Convert.ToBase64String(randomBytes);
    }
}
