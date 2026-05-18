using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
}