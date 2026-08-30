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
    public DbSet<RewardGrantRecord> RewardGrantRecords => Set<RewardGrantRecord>();
    public DbSet<RewardGrantItem> RewardGrantItems => Set<RewardGrantItem>();

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

        modelBuilder.Entity<RewardGrantRecord>(entity =>
        {
            // Player별 Request ID 중복 방지를 통한 보상 지급 멱등성 보장
            entity.HasIndex(record => new
            {
                record.PlayerId,
                record.RequestId
            })
                .IsUnique();

            entity.Property(record => record.Reason)
                .HasMaxLength(RewardGrantRecord.MaxReasonLength);

            entity.HasOne(record => record.Player)
                .WithMany()
                .HasForeignKey(record => record.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(record => record.Items)
                .WithOne(item => item.RewardGrantRecord)
                .HasForeignKey(item => item.RewardGrantRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RewardGrantItem>(entity =>
        {
            // 지급 이력에는 RewardType별로 합산된 Snapshot 한 건만 저장
            entity.HasIndex(item => new
            {
                item.RewardGrantRecordId,
                item.Type
            })
                .IsUnique();

            entity.Property(item => item.Type)
                .HasConversion<int>();
        });
    }
}
