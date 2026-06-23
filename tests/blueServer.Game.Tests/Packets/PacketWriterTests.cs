using System.Buffers.Binary;
using System.Text;
using blueServer.Game.Packets;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class PacketWriterTests
{
    [Fact]
    public void WriteBool_WritesOne_WhenValueIsTrue()
    {
        var writer = new PacketWriter();

        writer.WriteBool(true);

        Assert.Equal(new byte[] { 1 }, writer.ToArray());
    }

    [Fact]
    public void WriteBool_WritesZero_WhenValueIsFalse()
    {
        var writer = new PacketWriter();

        writer.WriteBool(false);

        Assert.Equal(new byte[] { 0 }, writer.ToArray());
    }

    [Fact]
    public void WriteUShort_WritesLittleEndianBytes()
    {
        var writer = new PacketWriter();

        writer.WriteUShort(0x1234);

        Assert.Equal(new byte[] { 0x34, 0x12 }, writer.ToArray());
    }

    [Fact]
    public void WriteInt_WritesLittleEndianBytes()
    {
        var writer = new PacketWriter();

        writer.WriteInt(0x12345678);

        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, writer.ToArray());
    }

    [Fact]
    public void WriteLong_WritesLittleEndianBytes()
    {
        var writer = new PacketWriter();

        writer.WriteLong(0x0102030405060708);

        Assert.Equal(
            new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 },
            writer.ToArray());
    }

    [Fact]
    public void WriteString_WritesLengthPrefixAndUtf8Bytes()
    {
        var writer = new PacketWriter();

        writer.WriteString("hello");

        var result = writer.ToArray();
        var length = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(0, 2));
        var text = Encoding.UTF8.GetString(result, 2, length);

        Assert.Equal(5, length);
        Assert.Equal("hello", text);
    }

    [Fact]
    public void WriteBytes_AppendsBytesAsIs()
    {
        var writer = new PacketWriter();

        writer.WriteBytes(new byte[] { 1, 2, 3 });

        Assert.Equal(new byte[] { 1, 2, 3 }, writer.ToArray());
    }
}
