using Xunit;

namespace blueServer.Game.IntegrationTests;

public sealed class PostgreSqlIntegrationFactAttribute : FactAttribute
{
    public const string ConnectionStringEnvironmentVariable =
        "BLUE_SERVER_INTEGRATION_CONNECTION_STRING";

    public PostgreSqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    ConnectionStringEnvironmentVariable)))
        {
            Skip =
                $"{ConnectionStringEnvironmentVariable} 환경 변수가 없어 PostgreSQL Integration Test를 건너뜁니다.";
        }
    }
}
