namespace blueServer.Infrastructure.Mails;

public sealed record MailClaimAllItemSummary(int ItemTemplateId, int GrantedQuantity);

/// <summary>
/// 저장된 Mail별 결과로 만드는 표시용 집계. 최신 보유량이나 Mail 상태는 재조회하지 않는다.
/// </summary>
public sealed record MailClaimAllSummary(
    MailClaimAllRequestStatus Status,
    DateTime? ProcessedAt,
    int GrantedGold,
    int GrantedGem,
    int CurrentGold,
    int CurrentGem,
    int ClaimedMailCount,
    int PartiallyClaimedMailCount,
    int NoCapacityMailCount,
    bool HasMore,
    IReadOnlyList<MailClaimAllItemSummary> Items)
{
    public static MailClaimAllSummary FromResult(MailClaimAllRequestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // 원래 첨부량/잔여량이 아닌 이번 요청의 실제 지급량만 합산한다.
        var items = result.Entries
            .SelectMany(entry => entry.Claim.Items)
            .Where(item => item.GrantedQuantity > 0)
            .GroupBy(item => item.ItemTemplateId)
            .OrderBy(group => group.Key)
            .Select(group => new MailClaimAllItemSummary(
                group.Key,
                group.Sum(item => item.GrantedQuantity)))
            .ToArray();

        return new MailClaimAllSummary(
            result.Status,
            result.ProcessedAt,
            result.GrantedGold,
            result.GrantedGem,
            result.CurrentGold,
            result.CurrentGem,
            result.ClaimedMailCount,
            result.Entries.Count(entry => entry.Claim.Status == MailClaimRequestStatus.PartiallyClaimed),
            result.Entries.Count(entry => entry.Claim.Status == MailClaimRequestStatus.NoCapacity),
            result.HasMore,
            Array.AsReadOnly(items));
    }
}
