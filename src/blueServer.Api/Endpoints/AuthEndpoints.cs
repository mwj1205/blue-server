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
            RegisterRequest request) =>
        {
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

            var player = new Player
            {
                Nickname = request.Nickname,
                Password = request.Password,

                Gold = 1000,
                Gem = 500
            };

            db.Players.Add(player);

            await db.SaveChangesAsync();

            return Results.Ok();
        });

        app.MapPost("/login", async (
            GameDbContext db,
            JwtService jwtService,
            LoginRequest request) =>
        {
            var player = await db.Players
                .FirstOrDefaultAsync(x =>
                    x.Nickname == request.Nickname &&
                    x.Password == request.Password);

            if (player is null)
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            "Invalid nickname or password"
                    });
            }

            var token =
                jwtService.GenerateToken(player);

            return Results.Ok(new
            {
                token
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