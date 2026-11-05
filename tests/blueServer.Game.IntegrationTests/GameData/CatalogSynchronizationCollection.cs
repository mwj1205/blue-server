using Xunit;

namespace blueServer.Game.IntegrationTests.GameData;

// 전체 ItemTemplate을 읽고 갱신하므로 다른 테스트의 템플릿 생성과 겹치지 않게 한다.
// 테스트 내부에서 명시적으로 시작하는 동시 작업에는 영향을 주지 않는다.
[CollectionDefinition(nameof(CatalogSynchronizationCollection), DisableParallelization = true)]
public sealed class CatalogSynchronizationCollection
{
}
