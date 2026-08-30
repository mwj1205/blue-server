using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace blueServer.Api.DesignTime;

public sealed class GameDbContextFactory
    : IDesignTimeDbContextFactory<GameDbContext>
{
    public GameDbContext CreateDbContext(string[] args)
    {
        // Migration 생성 시 Redis·Orleans와 분리된 EF Core Design-Time 구성
        var connectionString = Environment.GetEnvironmentVariable(
                "ConnectionStrings__Default") ??
            "Host=localhost;Database=bluearchive;Username=postgres";

        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new GameDbContext(options);
    }
}
