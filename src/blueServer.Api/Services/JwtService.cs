using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using blueServer.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace blueServer.Api.Services;

public class JwtService
{
    private readonly IConfiguration _configuration;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;
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
            Encoding.UTF8.GetBytes(
                _configuration["Jwt:Key"]!));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Issuer"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        // 빈 바이트 배열 준비
        var randomBytes = new byte[64];
        // 예측이 불가능한 암호학적 난수 생성기
        using var rng = RandomNumberGenerator.Create();
        // 빈 배열을 무작위 난수 데이터로 채움
        rng.GetBytes(randomBytes);
        // Base64 문자열로 변환하여 반환
        return Convert.ToBase64String(randomBytes);
    }
}
