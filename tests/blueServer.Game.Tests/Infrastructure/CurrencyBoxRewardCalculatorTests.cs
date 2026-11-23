using blueServer.Domain.Currencies;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure.GameData;
using blueServer.Infrastructure.Items;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class CurrencyBoxRewardCalculatorTests
{
    [Theory]
    [InlineData(CurrencyType.Gold, RewardType.Gold)]
    [InlineData(CurrencyType.Gem, RewardType.Gem)]
    public void Calculate_Fixed_MultipliesWithoutDrawing(CurrencyType currency, RewardType expected)
    {
        var random = new SequenceRandom([]);
        var calculator = new CurrencyBoxRewardCalculator(random);
        var box = new CurrencyBoxDefinition(3101, currency, CurrencyBoxAmountMode.Fixed, 100, 100);

        Assert.Equal(CurrencyReward.Create(expected, 300), calculator.Calculate(box, 3));
        Assert.Equal(0, random.Calls);
    }

    [Theory]
    [InlineData(CurrencyType.Gold, RewardType.Gold)]
    [InlineData(CurrencyType.Gem, RewardType.Gem)]
    public void Calculate_Random_DrawsPerBoxAndIncludesBothEndpoints(CurrencyType currency, RewardType expected)
    {
        var random = new SequenceRandom([20, 200, 35]);
        var calculator = new CurrencyBoxRewardCalculator(random);
        var box = new CurrencyBoxDefinition(3103, currency, CurrencyBoxAmountMode.UniformRandom, 20, 200);

        Assert.Equal(CurrencyReward.Create(expected, 255), calculator.Calculate(box, 3));
        Assert.Equal(3, random.Calls);
        Assert.All(random.Ranges, range => Assert.Equal((20, 200), range));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000000)]
    public void Calculate_InvalidQuantity_DoesNotDraw(int quantity)
    {
        var random = new SequenceRandom([]);
        var calculator = new CurrencyBoxRewardCalculator(random);
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(FixedBox(), quantity));
        Assert.Equal(0, random.Calls);
    }

    [Fact]
    public void Calculate_RejectsOverflowInsteadOfWrappingAmount()
    {
        var fixedCalculator = new CurrencyBoxRewardCalculator(new SequenceRandom([]));
        Assert.Throws<OverflowException>(() => fixedCalculator.Calculate(
            FixedBox() with { MinAmount = int.MaxValue, MaxAmount = int.MaxValue }, 2));

        var randomCalculator = new CurrencyBoxRewardCalculator(new SequenceRandom([int.MaxValue, 1]));
        Assert.Throws<OverflowException>(() => randomCalculator.Calculate(
            new CurrencyBoxDefinition(3103, CurrencyType.Gold, CurrencyBoxAmountMode.UniformRandom, 1, int.MaxValue), 2));
    }

    [Fact]
    public void Calculate_Cancellation_StopsBeforeNextDraw()
    {
        using var cancelled = new CancellationTokenSource();
        var random = new SequenceRandom([20, 20], () => cancelled.Cancel());
        var calculator = new CurrencyBoxRewardCalculator(random);
        var box = new CurrencyBoxDefinition(3103, CurrencyType.Gold, CurrencyBoxAmountMode.UniformRandom, 20, 200);
        Assert.Throws<OperationCanceledException>(() => calculator.Calculate(box, 2, cancelled.Token));
        Assert.Equal(1, random.Calls);
        Assert.Throws<OperationCanceledException>(() => calculator.Calculate(box, 1, cancelled.Token));
        Assert.Equal(1, random.Calls);
    }

    [Fact]
    public void Calculate_RejectsInvalidDefinitionAndBrokenRandomSource()
    {
        var calculator = new CurrencyBoxRewardCalculator(new SequenceRandom([]));
        Assert.Throws<ArgumentException>(() => calculator.Calculate(FixedBox() with { MaxAmount = 200 }, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(FixedBox() with { CurrencyType = (CurrencyType)99 }, 1));
        Assert.Throws<ArgumentException>(() => calculator.Calculate(FixedBox() with { AmountMode = (CurrencyBoxAmountMode)99 }, 1));

        var broken = new CurrencyBoxRewardCalculator(new SequenceRandom([201]));
        Assert.Throws<InvalidOperationException>(() => broken.Calculate(
            new CurrencyBoxDefinition(3103, CurrencyType.Gold, CurrencyBoxAmountMode.UniformRandom, 20, 200), 1));
    }

    [Fact]
    public void CryptographicRandom_RespectsRangeAndSupportsIntMaxValue()
    {
        var random = new CryptographicCurrencyBoxRandom();
        Assert.Equal(20, random.NextInclusive(20, 20));
        Assert.Equal(int.MaxValue, random.NextInclusive(int.MaxValue, int.MaxValue));
        Assert.InRange(random.NextInclusive(1, int.MaxValue), 1, int.MaxValue);
        for (var index = 0; index < 32; index++)
        {
            Assert.InRange(random.NextInclusive(20, 200), 20, 200);
        }
    }

    private static CurrencyBoxDefinition FixedBox() =>
        new(3101, CurrencyType.Gold, CurrencyBoxAmountMode.Fixed, 100, 100);

    private sealed class SequenceRandom(int[] values, Action? onDraw = null) : ICurrencyBoxRandom
    {
        public int Calls { get; private set; }
        public List<(int Min, int Max)> Ranges { get; } = [];

        public int NextInclusive(int minAmount, int maxAmount)
        {
            Ranges.Add((minAmount, maxAmount));
            var value = values[Calls++];
            onDraw?.Invoke();
            return value;
        }
    }
}
