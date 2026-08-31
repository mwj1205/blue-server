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
    public DbSet<Mail> Mails => Set<Mail>();
    public DbSet<MailAttachment> MailAttachments => Set<MailAttachment>();

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

        modelBuilder.Entity<Mail>(entity =>
        {
            entity.Property(mail => mail.SourceType)
                .HasConversion<int>();

            entity.Property(mail => mail.SourceId)
                .HasMaxLength(Mail.MaxSourceIdLength);

            entity.Property(mail => mail.Title)
                .HasMaxLength(Mail.MaxTitleLength);

            entity.Property(mail => mail.Body)
                .HasMaxLength(Mail.MaxBodyLength);

            // Player Mail 목록의 최신순 조회 지원
            entity.HasIndex(mail => new
            {
                mail.PlayerId,
                mail.SentAt
            });

            // 동일 발송 출처의 재시도에 대한 Mail 중복 생성 방지
            entity.HasIndex(mail => new
            {
                mail.PlayerId,
                mail.SourceType,
                mail.SourceId
            })
                .IsUnique();

            // 동일 Mail의 동시 수령 갱신 충돌 감지
            entity.Property(mail => mail.Version)
                .IsRowVersion();

            entity.HasOne(mail => mail.Player)
                .WithMany()
                .HasForeignKey(mail => mail.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(mail => mail.Attachments)
                .WithOne(attachment => attachment.Mail)
                .HasForeignKey(attachment => attachment.MailId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Mails_SourceType_Valid",
                    "\"SourceType\" IN (0, 1, 2, 3)");
                table.HasCheckConstraint(
                    "CK_Mails_SourceId_NotEmpty",
                    "length(btrim(\"SourceId\")) > 0");
                table.HasCheckConstraint(
                    "CK_Mails_ExpiresAt_After_SentAt",
                    "\"ExpiresAt\" IS NULL OR \"ExpiresAt\" > \"SentAt\"");
                table.HasCheckConstraint(
                    "CK_Mails_ReadAt_After_SentAt",
                    "\"ReadAt\" IS NULL OR \"ReadAt\" >= \"SentAt\"");
                table.HasCheckConstraint(
                    "CK_Mails_ClaimedAt_After_SentAt",
                    "\"ClaimedAt\" IS NULL OR \"ClaimedAt\" >= \"SentAt\"");
            });
        });

        modelBuilder.Entity<MailAttachment>(entity =>
        {
            // Mail에는 RewardType별로 합산된 Attachment Snapshot 한 건만 저장
            entity.HasIndex(attachment => new
            {
                attachment.MailId,
                attachment.Type
            })
                .IsUnique();

            entity.Property(attachment => attachment.Type)
                .HasConversion<int>();

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_MailAttachments_Amount_Positive",
                    "\"Amount\" > 0");
            });
        });
    }
}
