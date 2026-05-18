using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using blueServer.Api.DTOs;
using blueServer.Api.Exceptions;

namespace blueServer.Api.Services;

public class PlayerService
{
    private readonly GameDbContext _db;

    public PlayerService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<PlayerResponse> CreatePlayerAsync(CreatePlayerRequest request)
    {
        // 닉네임 중복 체크
        var exists = await _db.Players.AnyAsync(X => X.Nickname == request.Nickname); 

        if (exists)
        {
            throw new GameException("Nickname already exists");
        }

        // 초기 재화
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

    public async Task<PlayerResponse?> GetPlayerByIdAsync(long id)
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