using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using blueServer.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace blueServer.Game.Services;

public sealed class GameJwtValidator
{
    private readonly JwtOptions _options;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public GameJwtValidator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public GameJwtValidationResult Validate(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return GameJwtValidationResult.Fail("Token is required");
        }

        if (string.IsNullOrWhiteSpace(_options.Key) ||
            string.IsNullOrWhiteSpace(_options.Issuer) ||
            string.IsNullOrWhiteSpace(_options.EffectiveAudience))
        {
            return GameJwtValidationResult.Fail("Jwt options are not configured");
        }

        try
        {
            var principal = _tokenHandler.ValidateToken(
                accessToken,
                CreateValidationParameters(),
                out _);

            var playerIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var nickname = principal.FindFirst(ClaimTypes.Name)?.Value;

            if (!long.TryParse(playerIdClaim, out var playerId) ||
                string.IsNullOrWhiteSpace(nickname))
            {
                return GameJwtValidationResult.Fail("Required claims are missing");
            }

            return GameJwtValidationResult.Success(playerId, nickname);
        }
        catch (SecurityTokenException)
        {
            return GameJwtValidationResult.Fail("Token validation failed");
        }
        catch (ArgumentException)
        {
            return GameJwtValidationResult.Fail("Token validation failed");
        }
    }

    private TokenValidationParameters CreateValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.EffectiveAudience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_options.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }
}

public sealed record GameJwtValidationResult(
    bool IsSuccess,
    long PlayerId,
    string Nickname,
    string ErrorMessage)
{
    public static GameJwtValidationResult Success(
        long playerId,
        string nickname)
    {
        return new GameJwtValidationResult(
            true,
            playerId,
            nickname,
            string.Empty);
    }

    public static GameJwtValidationResult Fail(string errorMessage)
    {
        return new GameJwtValidationResult(
            false,
            0,
            string.Empty,
            errorMessage);
    }
}
