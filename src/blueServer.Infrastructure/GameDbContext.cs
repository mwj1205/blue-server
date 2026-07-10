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
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<PartySlot> PartySlots => Set<PartySlot>();
    public DbSet<StageTemplate> StageTemplates => Set<StageTemplate>();
    public DbSet<StageClearRecord> StageClearRecords => Set<StageClearRecord>();

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

        modelBuilder.Entity<Party>(entity =>
        {
            entity.HasIndex(party => new
                {
                    party.PlayerId,
                    party.PartyNo
                })
                .IsUnique();

            entity.HasMany(party => party.Slots)
                .WithOne(slot => slot.Party)
                .HasForeignKey(slot => slot.PartyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PartySlot>(entity =>
        {
            entity.HasIndex(slot => new
                {
                    slot.PartyId,
                    slot.SlotIndex
                })
                .IsUnique();

            entity.HasIndex(slot => new
                {
                    slot.PartyId,
                    slot.OwnedCharacterId
                })
                .IsUnique();

            entity.HasOne(slot => slot.OwnedCharacter)
                .WithMany()
                .HasForeignKey(slot => slot.OwnedCharacterId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StageTemplate>(entity =>
        {
            entity.HasIndex(stage => stage.Name)
                .IsUnique();

            entity.HasData(
                new StageTemplate
                {
                    Id = 1,
                    Name = "1-1",
                    RewardGold = 100,
                    RewardGem = 10
                });
        });

        modelBuilder.Entity<StageClearRecord>(entity =>
        {
            entity.HasIndex(record => new
                {
                    record.PlayerId,
                    record.StageTemplateId
                })
                .IsUnique();

            entity.Property(record => record.Version)
                .IsRowVersion();

            entity.HasOne(record => record.StageTemplate)
                .WithMany(stage => stage.ClearRecords)
                .HasForeignKey(record => record.StageTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
