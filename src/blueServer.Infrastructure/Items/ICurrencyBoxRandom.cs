using System.Security.Cryptography;

namespace blueServer.Infrastructure.Items;

public interface ICurrencyBoxRandom
{
    // 최소·최대값을 모두 포함하는 균등 정수 추첨.
    int NextInclusive(int minAmount, int maxAmount);
}

public sealed class CryptographicCurrencyBoxRandom : ICurrencyBoxRandom
{
    public int NextInclusive(int minAmount, int maxAmount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minAmount);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAmount, minAmount);

        // maxAmount + 1 대신 양수 범위의 폭을 사용해 int.MaxValue에서도 overflow를 피한다.
        var width = checked(maxAmount - minAmount + 1);
        return checked(minAmount + RandomNumberGenerator.GetInt32(width));
    }
}
