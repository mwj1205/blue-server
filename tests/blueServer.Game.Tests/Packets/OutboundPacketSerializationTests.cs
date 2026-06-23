using System.Buffers.Binary;
using System.Text;
using blueServer.Game.Packets;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class OutboundPacketSerializationTests
{
    [Fact]
    public void PingPacket_Serialize_WritesSizeAndOpcode()
    {
        var packet = new PingPacket().Serialize();

        AssertPacketHeader(packet, Opcode.Ping);
    }

    [Fact]
    public void PongPacket_Serialize_WritesSizeAndOpcode()
    {
        var packet = new PongPacket().Serialize();

        AssertPacketHeader(packet, Opcode.Pong);
    }

    [Fact]
    public void LoginResultPacket_Serialize_WritesExpectedPayload()
    {
        var packet = new LoginResultPacket
        {
            Success = true,
            Message = "Login Success"
        }.Serialize();

        AssertPacketHeader(packet, Opcode.LoginResult);

        var offset = PacketReader.HeaderSize;
        Assert.True(ReadBool(packet, ref offset));
        Assert.Equal("Login Success", ReadString(packet, ref offset));
        Assert.Equal(packet.Length, offset);
    }

    [Fact]
    public void ChatMessagePacket_Serialize_WritesExpectedPayload()
    {
        var packet = new ChatMessagePacket
        {
            Message = "[Arona]: hello"
        }.Serialize();

        AssertPacketHeader(packet, Opcode.ChatMessage);

        var offset = PacketReader.HeaderSize;
        Assert.Equal("[Arona]: hello", ReadString(packet, ref offset));
        Assert.Equal(packet.Length, offset);
    }

    [Fact]
    public void CharacterGachaResultPacket_Serialize_WritesExpectedPayload()
    {
        var packet = new CharacterGachaResultPacket
        {
            Success = true,
            Message = "Gacha success",
            OwnedCharacterId = 123,
            CharacterTemplateId = 7,
            CharacterName = "Shiroko",
            Rarity = 3,
            RemainingGem = 400
        }.Serialize();

        AssertPacketHeader(packet, Opcode.CharacterGachaResult);

        var offset = PacketReader.HeaderSize;
        Assert.True(ReadBool(packet, ref offset));
        Assert.Equal("Gacha success", ReadString(packet, ref offset));
        Assert.Equal(123, ReadLong(packet, ref offset));
        Assert.Equal(7, ReadInt(packet, ref offset));
        Assert.Equal("Shiroko", ReadString(packet, ref offset));
        Assert.Equal(3, ReadInt(packet, ref offset));
        Assert.Equal(400, ReadInt(packet, ref offset));
        Assert.Equal(packet.Length, offset);
    }

    private static void AssertPacketHeader(byte[] packet, Opcode expectedOpcode)
    {
        var size = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
        var opcode = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));

        Assert.Equal(packet.Length, size);
        Assert.Equal((ushort)expectedOpcode, opcode);
    }

    private static bool ReadBool(byte[] packet, ref int offset)
    {
        var value = packet[offset] == 1;
        offset += 1;
        return value;
    }

    private static int ReadInt(byte[] packet, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    private static long ReadLong(byte[] packet, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(offset, 8));
        offset += 8;
        return value;
    }

    private static string ReadString(byte[] packet, ref int offset)
    {
        var length = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(offset, 2));
        offset += 2;

        var value = Encoding.UTF8.GetString(packet, offset, length);
        offset += length;

        return value;
    }
}
