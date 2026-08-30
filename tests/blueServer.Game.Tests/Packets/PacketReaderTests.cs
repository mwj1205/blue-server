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

        Assert.Throws<PacketProtocolException>(() => new PacketReader(packet));
    }

    [Fact]
    public void Constructor_Throws_WhenHeaderSizeDoesNotMatchActualPacketLength()
    {
        var packet = new byte[PacketReader.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)Opcode.Ping);

        Assert.Throws<PacketProtocolException>(() => new PacketReader(packet));
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

        Assert.Throws<PacketProtocolException>(() => reader.ReadBool());
    }

    [Fact]
    public void ReadBool_Throws_WhenPayloadIsNotZeroOrOne()
    {
        var packet = CreatePacket(Opcode.MailList, writer =>
        {
            writer.WriteByte(2);
        });
        var reader = new PacketReader(packet);

        Assert.Throws<PacketProtocolException>(() => reader.ReadBool());
    }

    [Fact]
    public void ReadInt_ReturnsExpectedValue_WhenPayloadContainsInt()
    {
        var packet = CreatePacket(Opcode.PartyGet, writer =>
        {
            WriteInt(writer, 123);
        });

        var reader = new PacketReader(packet);

        Assert.Equal(123, reader.ReadInt());
    }

    [Fact]
    public void ReadLong_ReturnsExpectedValue_WhenPayloadContainsLong()
    {
        var packet = CreatePacket(Opcode.PartySave, writer =>
        {
            WriteLong(writer, 123456789L);
        });

        var reader = new PacketReader(packet);

        Assert.Equal(123456789L, reader.ReadLong());
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

        Assert.Throws<PacketProtocolException>(() => reader.ReadString());
    }

    [Fact]
    public void EnsureFullyRead_Throws_WhenPayloadHasTrailingBytes()
    {
        var packet = CreatePacket(Opcode.MailDetail, writer =>
        {
            writer.WriteByte(1);
        });
        var reader = new PacketReader(packet);

        Assert.Throws<PacketProtocolException>(() =>
            reader.EnsureFullyRead());
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

    private static void WriteInt(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];

        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteLong(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];

        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
