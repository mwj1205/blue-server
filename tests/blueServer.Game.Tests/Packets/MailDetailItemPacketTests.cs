using blueServer.Game.Packets;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class MailDetailItemPacketTests
{
    [Fact]
    public void DetailRequest_OptsInWithoutChangingLegacyFrame()
    {
        Assert.Equal(12, new MailDetailRequestPacket { MailId = 1 }.Serialize().Length);
        var bytes = new MailDetailRequestPacket { MailId = 1, IncludeItemAttachments = true }.Serialize();
        Assert.Equal(13, bytes.Length);
        Assert.True(MailDetailRequestPacket.Read(new PacketReader(bytes)).IncludeItemAttachments);
    }

    [Fact]
    public void DetailResult_AppendsItemQuantitiesOnlyWhenRequested()
    {
        var mail = new MailDetailPacketItem(1, "test", "body", DateTime.UtcNow,
            null, null, null, false, true, []);
        var legacy = new MailDetailResultPacket { Success = true, Mail = mail }.Serialize();
        var extended = new MailDetailResultPacket
        {
            Success = true,
            Mail = mail with { ItemAttachments = [new(3101, 20, 9, 11)] }
        }.Serialize();
        Assert.Equal(legacy.Length + 20, extended.Length);
        Assert.Equal(3101, BitConverter.ToInt32(extended, extended.Length - 16));
        Assert.Equal(20, BitConverter.ToInt32(extended, extended.Length - 12));
        Assert.Equal(9, BitConverter.ToInt32(extended, extended.Length - 8));
        Assert.Equal(11, BitConverter.ToInt32(extended, extended.Length - 4));
    }
}
