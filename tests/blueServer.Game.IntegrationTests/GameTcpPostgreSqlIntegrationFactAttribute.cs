using Xunit;

namespace blueServer.Game.IntegrationTests;

public sealed class GameTcpPostgreSqlIntegrationFactAttribute : FactAttribute
{
    public const string GameHostEnvironmentVariable =
        "BLUE_SERVER_GAME_HOST";
    public const string GamePortEnvironmentVariable =
        "BLUE_SERVER_GAME_PORT";

    public GameTcpPostgreSqlIntegrationFactAttribute()
    {
        var missingVariables = new[]
        {
            PostgreSqlIntegrationFactAttribute
                .ConnectionStringEnvironmentVariable,
            ApiPostgreSqlIntegrationFactAttribute
                .ApiBaseAddressEnvironmentVariable,
            GamePortEnvironmentVariable
        }
        .Where(variable => string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(variable)))
        .ToArray();

        if (missingVariables.Length > 0)
        {
            Skip =
                $"{string.Join(", ", missingVariables)} 환경 변수가 없어 Game TCP PostgreSQL Integration Test를 건너뜁니다.";
            return;
        }

        var gamePortValue = Environment.GetEnvironmentVariable(
            GamePortEnvironmentVariable);

        if (!int.TryParse(gamePortValue, out var gamePort) ||
            gamePort is < 1 or > ushort.MaxValue)
        {
            Skip =
                $"{GamePortEnvironmentVariable} 값이 올바른 TCP Port가 아닙니다: {gamePortValue}";
        }
    }
}
