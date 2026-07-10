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

    [Fact]
    public void OwnedCharacterListRequestPacket_Serialize_WritesSizeAndOpcode()
    {
        var packet = new OwnedCharacterListRequestPacket().Serialize();

        AssertPacketHeader(packet, Opcode.OwnedCharacterList);
    }

    [Fact]
    public void OwnedCharacterListResultPacket_Serialize_WritesExpectedPayload()
    {
        var packet = new OwnedCharacterListResultPacket
        {
            Success = true,
            Message = "Owned characters loaded",
            Characters =
            [
                new OwnedCharacterListPacketItem(
                    100,
                    7,
                    "Shiroko",
                    3,
                    "Striker",
                    12,
                    4,
                    250)
            ]
        }.Serialize();

        AssertPacketHeader(packet, Opcode.OwnedCharacterListResult);

        var offset = PacketReader.HeaderSize;
        Assert.True(ReadBool(packet, ref offset));
        Assert.Equal("Owned characters loaded", ReadString(packet, ref offset));
        Assert.Equal(1, ReadInt(packet, ref offset));
        Assert.Equal(100, ReadLong(packet, ref offset));
        Assert.Equal(7, ReadInt(packet, ref offset));
        Assert.Equal("Shiroko", ReadString(packet, ref offset));
        Assert.Equal(3, ReadInt(packet, ref offset));
        Assert.Equal("Striker", ReadString(packet, ref offset));
        Assert.Equal(12, ReadInt(packet, ref offset));
        Assert.Equal(4, ReadInt(packet, ref offset));
        Assert.Equal(250, ReadLong(packet, ref offset));
        Assert.Equal(packet.Length, offset);
    }

    [Fact]
    public void PartyGetRequestPacket_Serialize_WritesExpectedPayload()
    {
        var packet = new PartyGetRequestPacket
        {
            PartyNo = 1
        }.Serialize();

        AssertPacketHeader(packet, Opcode.PartyGet);

        var request = PartyGetRequestPacket.Read(new PacketReader(packet));

        Assert.Equal(1, request.PartyNo);
    }

    [Fact]
    public void PartySaveRequestPacket_Serialize_WritesExpectedPayload()
    {
        var packet = new PartySaveRequestPacket
        {
            PartyNo = 1,
            Name = "Main",
            Slots =
            [
                new PartySaveSlotPacketItem(1, 100),
                new PartySaveSlotPacketItem(2, 101)
            ]
        }.Serialize();

        AssertPacketHeader(packet, Opcode.PartySave);

        var request = PartySaveRequestPacket.Read(new PacketReader(packet));

        Assert.Equal(1, request.PartyNo);
        Assert.Equal("Main", request.Name);
        Assert.Equal(2, request.Slots.Count);
        Assert.Equal(new PartySaveSlotPacketItem(1, 100), request.Slots[0]);
        Assert.Equal(new PartySaveSlotPacketItem(2, 101), request.Slots[1]);
    }

    [Fact]
    public void PartyResultPacket_Serialize_WritesExpectedPayload()
    {
        var packet = new PartyResultPacket
        {
            Success = true,
            Message = "Party loaded",
            PartyNo = 1,
            Name = "Main",
            Slots =
            [
                new PartySlotPacketItem(
                    1,
                    100,
                    7,
                    "Shiroko",
                    3,
                    "Striker",
                    12,
                    4,
                    250)
            ]
        }.Serialize();

        AssertPacketHeader(packet, Opcode.PartyResult);

        var offset = PacketReader.HeaderSize;
        Assert.True(ReadBool(packet, ref offset));
        Assert.Equal("Party loaded", ReadString(packet, ref offset));
        Assert.Equal(1, ReadInt(packet, ref offset));
        Assert.Equal("Main", ReadString(packet, ref offset));
        Assert.Equal(1, ReadInt(packet, ref offset));
        Assert.Equal(1, ReadInt(packet, ref offset));
        Assert.Equal(100, ReadLong(packet, ref offset));
        Assert.Equal(7, ReadInt(packet, ref offset));
        Assert.Equal("Shiroko", ReadString(packet, ref offset));
        Assert.Equal(3, ReadInt(packet, ref offset));
        Assert.Equal("Striker", ReadString(packet, ref offset));
        Assert.Equal(12, ReadInt(packet, ref offset));
        Assert.Equal(4, ReadInt(packet, ref offset));
        Assert.Equal(250, ReadLong(packet, ref offset));
        Assert.Equal(packet.Length, offset);
    }

    [Fact]
    public void StageClearRequestPacket_Serialize_WritesExpectedPayload()
    {
        var packet = new StageClearRequestPacket
        {
            StageTemplateId = 1,
            PartyNo = 1
        }.Serialize();

        AssertPacketHeader(packet, Opcode.StageClear);

        var request = StageClearRequestPacket.Read(new PacketReader(packet));

        Assert.Equal(1, request.StageTemplateId);
        Assert.Equal(1, request.PartyNo);
    }

    [Fact]
    public void StageClearResultPacket_Serialize_WritesExpectedPayload()
    {
        var packet = new StageClearResultPacket
        {
            Success = true,
            Message = "Stage clear success",
            StageTemplateId = 1,
            StageName = "1-1",
            PartyNo = 1,
            RewardGold = 100,
            RewardGem = 10,
            CurrentGold = 1100,
            CurrentGem = 510,
            ClearCount = 1
        }.Serialize();

        AssertPacketHeader(packet, Opcode.StageClearResult);

        var offset = PacketReader.HeaderSize;
        Assert.True(ReadBool(packet, ref offset));
        Assert.Equal("Stage clear success", ReadString(packet, ref offset));
        Assert.Equal(1, ReadInt(packet, ref offset));
        Assert.Equal("1-1", ReadString(packet, ref offset));
        Assert.Equal(1, ReadInt(packet, ref offset));
        Assert.Equal(100, ReadInt(packet, ref offset));
        Assert.Equal(10, ReadInt(packet, ref offset));
        Assert.Equal(1100, ReadInt(packet, ref offset));
        Assert.Equal(510, ReadInt(packet, ref offset));
        Assert.Equal(1, ReadInt(packet, ref offset));
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
