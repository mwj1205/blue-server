namespace blueServer.Domain.Entities;

public class Player
{
    public const int InitialGold = 1000;
    public const int InitialGem = 500;

    public long Id { get; set; }
    public string Password { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int Gold { get; set; }
    public int Gem { get; set; }
    public uint Version { get; set; }

    public ICollection<OwnedCharacter> OwnedCharacters { get; set; } = new List<OwnedCharacter>();
    public ICollection<Party> Parties { get; set; } = new List<Party>();

    public static Player Create(
        string nickname,
        string password = "")
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            throw new ArgumentException(
                "Nickname is required.",
                nameof(nickname));
        }

        return new Player
        {
            Nickname = nickname,
            Password = password,
            Gold = InitialGold,
            Gem = InitialGem
        };
    }

    public bool TrySpendGems(int amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero.");
        }

        if (Gem < amount)
        {
            return false;
        }

        Gem -= amount;
        return true;
    }
}
