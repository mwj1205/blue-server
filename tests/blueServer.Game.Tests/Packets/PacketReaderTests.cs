using System.Buffers.Binary;
using System.Text;
using blueServer.Game.Packets;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class PacketReaderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Constructor_Throws_WhenPacketIsShorterThanHeader(int length)
    {
        var packet = new byte[length];

        Assert.Throws<ArgumentException>(() => new PacketReader(packet));
    }

    [Fact]
    public void Constructor_Throws_WhenHeaderSizeDoesNotMatchActualPacketLength()
    {
        var packet = new byte[PacketReader.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)Opcode.Ping);

        Assert.Throws<ArgumentException>(() => new PacketReader(packet));
    }

    [Fact]
    public void Constructor_ReadsSizeAndOpcode_WhenPacketHeaderIsValid()
    {
        var packet = CreatePacket(Opcode.Ping);

        var reader = new PacketReader(packet);

        Assert.Equal(packet.Length, reader.Size);
        Assert.Equal(Opcode.Ping, reader.Opcode);
    }

    [Fact]
    public void ReadBool_ReturnsTrue_WhenPayloadByteIsOne()
    {
        var packet = CreatePacket(Opcode.LoginResult, writer =>
        {
            writer.WriteByte(1);
        });

        var reader = new PacketReader(packet);

        Assert.True(reader.ReadBool());
    }

    [Fact]
    public void ReadBool_Throws_WhenPayloadIsMissing()
    {
        var packet = CreatePacket(Opcode.LoginResult);
        var reader = new PacketReader(packet);

        Assert.Throws<InvalidOperationException>(() => reader.ReadBool());
    }

    [Fact]
    public void ReadString_ReturnsExpectedString_WhenPayloadContainsValidString()
    {
        const string expected = "Arona";
        var packet = CreatePacket(Opcode.Login, writer =>
        {
            WriteString(writer, expected);
        });

        var reader = new PacketReader(packet);

        Assert.Equal(expected, reader.ReadString());
    }

    [Fact]
    public void ReadString_Throws_WhenDeclaredStringLengthExceedsPayload()
    {
        var packet = CreatePacket(Opcode.Login, writer =>
        {
            Span<byte> lengthBytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(lengthBytes, 10);
            writer.Write(lengthBytes);
            writer.WriteByte((byte)'A');
        });

        var reader = new PacketReader(packet);

        Assert.Throws<InvalidOperationException>(() => reader.ReadString());
    }

    private static byte[] CreatePacket(Opcode opcode, Action<MemoryStream>? writePayload = null)
    {
        using var payload = new MemoryStream();
        writePayload?.Invoke(payload);

        var packetSize = checked((ushort)(PacketReader.HeaderSize + payload.Length));
        var packet = new byte[packetSize];

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), packetSize);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)opcode);
        payload.ToArray().CopyTo(packet.AsSpan(PacketReader.HeaderSize));

        return packet;
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> lengthBytes = stackalloc byte[2];

        BinaryPrimitives.WriteUInt16LittleEndian(lengthBytes, checked((ushort)bytes.Length));
        stream.Write(lengthBytes);
        stream.Write(bytes);
    }
}
