using System;
using BlueServer.Client.Models;
using BlueServer.Client.Protocol;
using NUnit.Framework;

namespace BlueServer.Client.Tests
{
    public sealed class InventoryListPacketCodecTests
    {
        [Test]
        public void Request_ContainsOnlyRequestId()
        {
            var id = Guid.NewGuid();
            var bytes = InventoryListPacketCodec.BuildRequest(id);
            Assert.That(bytes.Length, Is.EqualTo(42));
            var reader = new GamePacketReader(bytes);
            Assert.That(reader.Opcode, Is.EqualTo(GameOpcode.InventoryList));
            Assert.That(reader.ReadString(), Is.EqualTo(id.ToString("D")));
            reader.EnsureFullyConsumed();
            Assert.Throws<ArgumentException>(() => InventoryListPacketCodec.BuildRequest(Guid.Empty));
        }

        [TestCase(0)]
        [TestCase(310)]
        [TestCase(311)]
        [TestCase(10000)]
        public void Response_PublishesOnlyCompleteInventory(int total)
        {
            var id = Guid.NewGuid();
            var assembler = new InventoryListAssembler(id);
            var parts = Math.Max(1, (total + 309) / 310);
            InventoryListResponse response = null;
            for (var part = 0; part < parts; part++)
            {
                var completed = assembler.Append(Frame(id, 0, part, parts, total), out response);
                Assert.That(completed, Is.EqualTo(part == parts - 1));
                if (!completed) Assert.That(response, Is.Null);
            }
            Assert.That(response.Success, Is.True);
            Assert.That(response.RequestId, Is.EqualTo(id));
            Assert.That(response.Items.Count, Is.EqualTo(total));
            for (var index = 0; index < total; index++)
            {
                Assert.That(response.Items[index].ItemTemplateId, Is.EqualTo(index + 1));
                Assert.That(response.Items[index].Quantity, Is.EqualTo(7));
                Assert.That(response.Items[index].Type, Is.EqualTo(InventoryItemType.Consumable));
                Assert.That(response.Items[index].IsActive, Is.False);
            }
            Assert.Throws<GameProtocolException>(() => assembler.Append(Frame(id, 0, 0, 1, 0), out response));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void Response_ParsesFailureAsEmptyCompletedResponse(int status)
        {
            var id = Guid.NewGuid();
            InventoryListResponse response;
            Assert.That(new InventoryListAssembler(id).Append(Frame(id, status, 0, 1, 0), out response), Is.True);
            Assert.That(response.Success, Is.False);
            Assert.That((int)response.Status, Is.EqualTo(status));
            Assert.That(response.Items, Is.Empty);
        }

        [TestCase(0, 0, 34, 10001)]
        [TestCase(0, 1, 1, 1)]
        [TestCase(0, 0, 2, 1)]
        [TestCase(3, 0, 1, 0)]
        [TestCase(1, 0, 1, 1)]
        public void Response_RejectsInvalidEnvelope(int status, int part, int parts, int total)
        {
            var id = Guid.NewGuid();
            InventoryListResponse response;
            Assert.Throws<GameProtocolException>(() =>
                new InventoryListAssembler(id).Append(Frame(id, status, part, parts, total), out response));
        }

        [Test]
        public void Response_RejectsMismatchedOrDuplicatePartsAndCannotRecoverPartialResult()
        {
            var id = Guid.NewGuid();
            foreach (var second in new[]
            {
                Frame(Guid.NewGuid(), 0, 1, 2, 311),
                Frame(id, 0, 0, 2, 311),
                Frame(id, 0, 1, 2, 312),
                Frame(id, 0, 1, 2, 311, 1)
            })
            {
                var assembler = new InventoryListAssembler(id);
                InventoryListResponse response;
                Assert.That(assembler.Append(Frame(id, 0, 0, 2, 311), out response), Is.False);
                Assert.That(response, Is.Null);
                Assert.Throws<GameProtocolException>(() => assembler.Append(second, out response));
                Assert.That(response, Is.Null);
                Assert.Throws<GameProtocolException>(() => assembler.Append(Frame(id, 0, 1, 2, 311), out response));
            }
        }

        [TestCase(0, 3)]
        [TestCase(1000000, 3)]
        [TestCase(7, 5)]
        public void Response_RejectsInvalidItemValues(int quantity, int type)
        {
            var id = Guid.NewGuid();
            var bytes = Frame(id, 0, 0, 1, 1);
            BitConverter.GetBytes(quantity).CopyTo(bytes, 66);
            BitConverter.GetBytes(type).CopyTo(bytes, 70);
            InventoryListResponse response;
            Assert.Throws<GameProtocolException>(() => new InventoryListAssembler(id).Append(bytes, out response));
        }

        [Test]
        public void Response_RejectsTruncatedTrailingAndOversizedFrames()
        {
            var id = Guid.NewGuid();
            var valid = Frame(id, 0, 0, 1, 1);
            var truncated = new byte[valid.Length - 1];
            Array.Copy(valid, truncated, truncated.Length);
            BitConverter.GetBytes((ushort)truncated.Length).CopyTo(truncated, 0);
            var trailing = new byte[valid.Length + 1];
            Array.Copy(valid, trailing, valid.Length);
            BitConverter.GetBytes((ushort)trailing.Length).CopyTo(trailing, 0);
            InventoryListResponse response;
            foreach (var bytes in new[] { truncated, trailing, new byte[4097] })
            {
                Assert.Throws<GameProtocolException>(() => new InventoryListAssembler(id).Append(bytes, out response));
            }
        }

        private static byte[] Frame(Guid id, int status, int part, int parts, int total, int firstId = 0)
        {
            using (var writer = new GamePacketWriter())
            {
                writer.WriteString(id.ToString("D"));
                writer.WriteInt(status);
                writer.WriteInt(part);
                writer.WriteInt(parts);
                writer.WriteInt(total);
                var count = Math.Max(0, Math.Min(310, total - part * 310));
                writer.WriteInt(count);
                for (var index = 0; index < count; index++)
                {
                    writer.WriteInt(firstId == 0 ? part * 310 + index + 1 : firstId + index);
                    writer.WriteInt(7);
                    writer.WriteInt(3);
                    writer.WriteBool(false);
                }
                return writer.BuildPacket(GameOpcode.InventoryListResult);
            }
        }
    }
}
