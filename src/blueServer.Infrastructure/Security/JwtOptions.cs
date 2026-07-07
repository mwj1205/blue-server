namespace blueServer.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int AccessTokenDays { get; set; } = 7;

    // fallback
    public string EffectiveAudience =>
        string.IsNullOrWhiteSpace(Audience) ? Issuer : Audience;
}
