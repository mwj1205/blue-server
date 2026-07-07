using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using blueServer.Game.Services;
using blueServer.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace blueServer.Game.Tests.Services;

public sealed class GameJwtValidatorTests
{
    [Fact]
    public void Validate_ReturnsPlayerInfo_WhenTokenIsValid()
    {
        var options = CreateOptions();
        var validator = new GameJwtValidator(Options.Create(options));
        var token = CreateToken(options, 10, "sensei");

        var result = validator.Validate(token);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.PlayerId);
        Assert.Equal("sensei", result.Nickname);
    }

    [Fact]
    public void Validate_ReturnsFailure_WhenTokenIsInvalid()
    {
        var validator = new GameJwtValidator(Options.Create(CreateOptions()));

        var result = validator.Validate("invalid-token");

        Assert.False(result.IsSuccess);
    }

    private static JwtOptions CreateOptions()
    {
        return new JwtOptions
        {
            Key = "01234567890123456789012345678901",
            Issuer = "blue-server",
            Audience = "blue-game",
            AccessTokenDays = 7
        };
    }

    private static string CreateToken(
        JwtOptions options,
        long playerId,
        string nickname)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.Key));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.EffectiveAudience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, playerId.ToString()),
                new Claim(ClaimTypes.Name, nickname)
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
