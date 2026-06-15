/***
  * File: GameDbContext.cs
  * Role: DB 연결
          테이블 관리
          쿼리 실행
          변경 사항 저장
***/

using blueServer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure;

public class GameDbContext : DbContext
{
    public GameDbContext(DbContextOptions<GameDbContext> options) : base(options)
    {
    }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<OwnedCharacter> OwnedCharacters => Set<OwnedCharacter>();
    public DbSet<CharacterTemplate> CharacterTemplates => Set<CharacterTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasIndex(player => player.Nickname)
                .IsUnique();

            entity.Property(player => player.Version)
                .IsRowVersion();
        });

        modelBuilder.Entity<OwnedCharacter>()
            .Property(character => character.Version)
            .IsRowVersion();
    }
}
