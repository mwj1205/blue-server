using System.Security.Claims;
using blueServer.Api.DTOs;
using blueServer.Api.Services;
using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder
        MapAuthEndpoints(
            this IEndpointRouteBuilder app)
    {
        app.MapPost("/register", async (
            GameDbContext db,
            PasswordService passwordService,
            RegisterRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Nickname))
            {
                return Results.BadRequest(new
                {
                    message = "Nickname is required"
                });
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new
                {
                    message = "Password is required"
                });
            }

            var exists = await db.Players
                .AnyAsync(x =>
                    x.Nickname == request.Nickname);

            if (exists)
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            "Nickname already exists"
                    });
            }

            var player = Player.Create(
                request.Nickname,
                passwordService.HashPassword(request.Password));

            db.Players.Add(player);

            await db.SaveChangesAsync();

            return Results.Ok();
        });

        app.MapPost("/login", async (
            GameDbContext db,
            JwtService jwtService,
            RefreshTokenService refreshTokenService,
            PasswordService passwordService,
            LoginRequest request) =>
        {
            var player = await db.Players
                .FirstOrDefaultAsync(x =>
                    x.Nickname == request.Nickname);

            if (player is null ||
                !passwordService.VerifyPassword(request.Password, player.Password))
            {
                return Results.BadRequest(
                    new
                    {
                        message = "Invalid nickname or password"
                    });
            }

            var accessToken = jwtService.GenerateToken(player);

            var refreshToken = jwtService.GenerateRefreshToken();

            // redis에 refresh token 저장
            await refreshTokenService
                .SaveRefreshTokenAsync(
                    player.Id,
                    refreshToken);

            return Results.Ok(new
            {
                accessToken,
                refreshToken
            });
        });

        app.MapPost("/refresh", async (
            JwtService jwtService,
            RefreshTokenService refreshTokenService,
            GameDbContext db,
            RefreshRequest request) =>
        {
            // 저장된 refrest token을 가져옴
            var savedRefreshToken =
                await refreshTokenService
                    .GetRefreshTokenAsync(
                        request.PlayerId);

            if (savedRefreshToken is null ||
                savedRefreshToken != request.RefreshToken)
            {
                return Results.BadRequest(new
                {
                    message = "Invalid refresh token"
                });
            }

            var player = await db.Players
                .FindAsync(request.PlayerId);

            if (player is null)
            {
                return Results.BadRequest(new
                {
                    message = "Player not found"
                });
            }

            var newAccessToken =
                jwtService.GenerateToken(player);

            return Results.Ok(new
            {
                accessToken = newAccessToken
            });
        });

        app.MapGet("/me", (
            ClaimsPrincipal user) =>
        {
            var userId =
                user.FindFirstValue(
                    ClaimTypes.NameIdentifier);

            var nickname =
                user.FindFirstValue(
                    ClaimTypes.Name);

            return Results.Ok(new
            {
                userId,
                nickname
            });
        })
        .RequireAuthorization();

        return app;
    }
}
