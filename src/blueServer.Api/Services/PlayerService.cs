using blueServer.Api.DTOs;
using blueServer.Api.Exceptions;
using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Api.Services;

public class PlayerService
{
    private readonly GameDbContext _db;

    public PlayerService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<PlayerResponse> CreatePlayerAsync(
        CreatePlayerRequest request)
    {
        var exists = await _db.Players
            .AnyAsync(x => x.Nickname == request.Nickname);

        if (exists)
        {
            throw new GameException(
                "Nickname already exists");
        }

        var player = new Player
        {
            Nickname = request.Nickname,
            Gold = 1000,
            Gem = 500
        };

        _db.Players.Add(player);

        await _db.SaveChangesAsync();

        return new PlayerResponse
        {
            Id = player.Id,
            Nickname = player.Nickname,
            Gold = player.Gold,
            Gem = player.Gem
        };
    }

    public async Task<PlayerResponse?> GetPlayerAsync(
        long id)
    {
        var player = await _db.Players.FindAsync(id);

        if (player is null)
        {
            return null;
        }

        return new PlayerResponse
        {
            Id = player.Id,
            Nickname = player.Nickname,
            Gold = player.Gold,
            Gem = player.Gem
        };
    }
}