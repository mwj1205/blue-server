using System.Text;
using blueServer.Infrastructure.GameData;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class LocalizationCatalogJsonLoaderTests
{
    [Fact]
    public async Task LoadAsync_RejectsDuplicateKeys()
    {
        await using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes(
                """
                {
                  "schemaVersion": 1,
                  "locale": "ko-KR",
                  "entries": [
                    { "key": "item.material.name", "value": "성장 재료" },
                    { "key": "item.material.name", "value": "중복 이름" }
                  ]
                }
                """));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => LocalizationCatalogJsonLoader.LoadAsync(stream));

        Assert.Contains("duplicate key 'item.material.name'", exception.Message);
    }
}
