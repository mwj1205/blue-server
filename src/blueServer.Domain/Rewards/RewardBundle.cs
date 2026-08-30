namespace blueServer.Domain.Rewards;

public sealed class RewardBundle
{
    private readonly IReadOnlyList<RewardItem> _items;

    private RewardBundle(IReadOnlyList<RewardItem> items)
    {
        _items = items;
    }

    public IReadOnlyList<RewardItem> Items => _items;

    public static RewardBundle Create(IEnumerable<RewardItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var itemArray = items.ToArray();

        if (itemArray.Length == 0)
        {
            throw new ArgumentException(
                "At least one reward item is required.",
                nameof(items));
        }

        if (itemArray.Any(item => item is null))
        {
            throw new ArgumentException(
                "Reward items must not contain null.",
                nameof(items));
        }

        return new RewardBundle(Array.AsReadOnly(itemArray));
    }

    public static RewardBundle Create(params RewardItem[] items)
    {
        return Create((IEnumerable<RewardItem>)items);
    }
}
