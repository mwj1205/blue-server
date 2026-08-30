using Xunit;

namespace blueServer.Game.IntegrationTests;

public sealed class ApiPostgreSqlIntegrationFactAttribute : FactAttribute
{
    public const string ApiBaseAddressEnvironmentVariable =
        "BLUE_SERVER_API_BASE_ADDRESS";

    public ApiPostgreSqlIntegrationFactAttribute()
    {
        var missingVariables = new[]
        {
            PostgreSqlIntegrationFactAttribute
                .ConnectionStringEnvironmentVariable,
            ApiBaseAddressEnvironmentVariable
        }
        .Where(variable => string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(variable)))
        .ToArray();

        if (missingVariables.Length > 0)
        {
            Skip =
                $"{string.Join(", ", missingVariables)} 환경 변수가 없어 API PostgreSQL Integration Test를 건너뜁니다.";
        }
    }
}
