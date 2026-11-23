using System;
using BlueServer.Client.Protocol;
using NUnit.Framework;

namespace BlueServer.Client.Tests
{
    public sealed class ItemUsePacketCodecTests
    {
        [Test]
        public void UseRequest_ContainsItemQuantityAndStableId()
        {
            var id = Guid.NewGuid();
            var bytes = ItemUsePacketCodec.BuildUseRequest(3101, 20, id);
            Assert.That(bytes.Length, Is.EqualTo(50));
            var reader = new GamePacketReader(bytes);
            Assert.That(reader.Opcode, Is.EqualTo(GameOpcode.CurrencyBoxUse));
            Assert.That(reader.ReadInt(), Is.EqualTo(3101));
            Assert.That(reader.ReadInt(), Is.EqualTo(20));
            Assert.That(reader.ReadString(), Is.EqualTo(id.ToString("D")));
            reader.EnsureFullyConsumed();
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(1000000)]
        public void UseRequest_RejectsInvalidQuantity(int quantity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ItemUsePacketCodec.BuildUseRequest(3101, quantity, Guid.NewGuid()));
        }

        [Test]
        public void UseResponse_ParsesRewardAndRejectsWrongRequestId()
        {
            var id = Guid.NewGuid();
            byte[] bytes;
            using (var writer = new GamePacketWriter())
            {
                writer.WriteInt(0); writer.WriteString(id.ToString("D")); writer.WriteBool(true);
                writer.WriteInt(3101); writer.WriteInt(20); writer.WriteInt(999979);
                writer.WriteInt(1); writer.WriteInt(2000); writer.WriteInt(3000); writer.WriteInt(500);
                writer.WriteLong(0);
                bytes = writer.BuildPacket(GameOpcode.CurrencyBoxUseResult);
            }
            var result = ItemUsePacketCodec.ParseUse(bytes, id);
            Assert.That(result.Success, Is.True);
            Assert.That(result.QuantityAfter, Is.EqualTo(999979));
            Assert.That(result.GrantedAmount, Is.EqualTo(2000));
            Assert.That(result.CurrentGold, Is.EqualTo(3000));
            Assert.Throws<GameProtocolException>(() => ItemUsePacketCodec.ParseUse(bytes, Guid.NewGuid()));
            Array.Resize(ref bytes, bytes.Length - 1);
            Assert.Throws<GameProtocolException>(() => ItemUsePacketCodec.ParseUse(bytes, id));
        }

        [Test]
        public void ClaimResponse_AcceptsPartialAndNoCapacityWithoutCompletionTime()
        {
            foreach (var status in new[] { 7, 8 })
            {
                var id = Guid.NewGuid();
                using (var writer = new GamePacketWriter())
                {
                    writer.WriteBool(true); writer.WriteInt(status); writer.WriteString("partial");
                    writer.WriteBool(false); writer.WriteInt(1000); writer.WriteInt(500);
                    writer.WriteString(id.ToString("D")); writer.WriteBool(true); writer.WriteLong(0);
                    writer.WriteInt(0); writer.WriteInt(0); writer.WriteInt(1);
                    writer.WriteInt(3101); writer.WriteInt(status == 7 ? 9 : 0);
                    writer.WriteInt(status == 7 ? 11 : 20); writer.WriteInt(999999);
                    var result = ItemUsePacketCodec.ParseClaim(writer.BuildPacket(GameOpcode.MailClaimResult), id);
                    Assert.That(result.Success, Is.True);
                    Assert.That(result.Items[0].QuantityAfter, Is.EqualTo(999999));
                }
            }
        }

        [Test]
        public void DetailItems_RejectsInconsistentRemainder()
        {
            using (var writer = new GamePacketWriter())
            {
                writer.WriteInt(1); writer.WriteInt(3101); writer.WriteInt(20);
                writer.WriteInt(9); writer.WriteInt(12);
                Assert.Throws<GameProtocolException>(() => ItemUsePacketCodec.ReadDetailItems(
                    new GamePacketReader(writer.BuildPacket(GameOpcode.MailDetailResult))));
            }
        }
    }
}
