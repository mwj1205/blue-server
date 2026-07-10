using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Services;

public sealed class StageClearService
{
    private readonly GameDbContext _db;

    public StageClearService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<StageClearResult> ClearAsync(
        long playerId,
        int stageTemplateId,
        int partyNo,
        CancellationToken cancellationToken)
    {
        var validationMessage = ValidateRequest(stageTemplateId, partyNo);

        if (validationMessage is not null)
        {
            return StageClearResult.Fail(
                validationMessage,
                stageTemplateId,
                partyNo);
        }

        var player = await _db.Players
            .FirstOrDefaultAsync(
                player => player.Id == playerId,
                cancellationToken);

        if (player is null)
        {
            return StageClearResult.Fail(
                "Player not found",
                stageTemplateId,
                partyNo);
        }

        var stage = await _db.StageTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                stage => stage.Id == stageTemplateId,
                cancellationToken);

        if (stage is null)
        {
            return StageClearResult.Fail(
                "Stage not found",
                stageTemplateId,
                partyNo,
                currentGold: player.Gold,
                currentGem: player.Gem);
        }

        var party = await _db.Parties
            .AsNoTracking()
            .Where(party =>
                party.PlayerId == playerId &&
                party.PartyNo == partyNo)
            .Select(party => new
            {
                SlotCount = party.Slots.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (party is null)
        {
            return StageClearResult.Fail(
                "Party not found",
                stageTemplateId,
                partyNo,
                stage.Name,
                player.Gold,
                player.Gem);
        }

        if (party.SlotCount == 0)
        {
            return StageClearResult.Fail(
                "Party has no characters",
                stageTemplateId,
                partyNo,
                stage.Name,
                player.Gold,
                player.Gem);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var clearedAt = DateTime.UtcNow;
            var record = await _db.StageClearRecords
                .FirstOrDefaultAsync(
                    record =>
                        record.PlayerId == playerId &&
                        record.StageTemplateId == stageTemplateId,
                    cancellationToken);

            if (record is null)
            {
                record = StageClearRecord.Create(
                    playerId,
                    stageTemplateId,
                    clearedAt);

                _db.StageClearRecords.Add(record);
            }
            else
            {
                record.RecordClear(clearedAt);
            }

            player.AddGold(stage.RewardGold);
            player.AddGems(stage.RewardGem);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return StageClearResult.Success(
                stageTemplateId,
                stage.Name,
                partyNo,
                stage.RewardGold,
                stage.RewardGem,
                player.Gold,
                player.Gem,
                record.ClearCount);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);

            return StageClearResult.Fail(
                "Another request changed the stage clear data. Please try again.",
                stageTemplateId,
                partyNo,
                stage.Name,
                player.Gold,
                player.Gem);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static string? ValidateRequest(
        int stageTemplateId,
        int partyNo)
    {
        if (stageTemplateId <= 0)
        {
            return "Stage template id must be greater than zero";
        }

        if (partyNo is < Party.MinPartyNo or > Party.MaxPartyNo)
        {
            return $"Party no must be between {Party.MinPartyNo} and {Party.MaxPartyNo}";
        }

        return null;
    }
}

public sealed record StageClearResult(
    bool IsSuccess,
    string Message,
    int StageTemplateId,
    string StageName,
    int PartyNo,
    int RewardGold,
    int RewardGem,
    int CurrentGold,
    int CurrentGem,
    int ClearCount)
{
    public static StageClearResult Success(
        int stageTemplateId,
        string stageName,
        int partyNo,
        int rewardGold,
        int rewardGem,
        int currentGold,
        int currentGem,
        int clearCount)
    {
        return new StageClearResult(
            true,
            "Stage clear success",
            stageTemplateId,
            stageName,
            partyNo,
            rewardGold,
            rewardGem,
            currentGold,
            currentGem,
            clearCount);
    }

    public static StageClearResult Fail(
        string message,
        int stageTemplateId,
        int partyNo,
        string stageName = "",
        int currentGold = 0,
        int currentGem = 0)
    {
        return new StageClearResult(
            false,
            message,
            stageTemplateId,
            stageName,
            partyNo,
            0,
            0,
            currentGold,
            currentGem,
            0);
    }
}
