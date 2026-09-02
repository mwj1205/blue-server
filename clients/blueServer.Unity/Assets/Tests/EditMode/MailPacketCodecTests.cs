using System;
using BlueServer.Client.Models;
using BlueServer.Client.Protocol;
using NUnit.Framework;

namespace BlueServer.Client.Tests
{
    public sealed class MailPacketCodecTests
    {
        private static readonly DateTime SentAt =
            new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc);

        [Test]
        public void MailRequests_WriteExpectedPayload()
        {
            var cursor = new MailListCursor(SentAt, 41);
            var listReader = new GamePacketReader(
                MailPacketCodec.BuildListRequest(20, cursor));

            Assert.That(listReader.Opcode, Is.EqualTo(GameOpcode.MailList));
            Assert.That(listReader.ReadInt(), Is.EqualTo(20));
            Assert.That(listReader.ReadBool(), Is.True);
            Assert.That(
                listReader.ReadLong(),
                Is.EqualTo(new DateTimeOffset(SentAt).ToUnixTimeMilliseconds()));
            Assert.That(listReader.ReadLong(), Is.EqualTo(41));
            listReader.EnsureFullyConsumed();

            AssertMailIdRequest(
                MailPacketCodec.BuildDetailRequest(42),
                GameOpcode.MailDetail,
                42);
            AssertMailIdRequest(
                MailPacketCodec.BuildReadRequest(43),
                GameOpcode.MailRead,
                43);
            AssertMailIdRequest(
                MailPacketCodec.BuildClaimRequest(44),
                GameOpcode.MailClaim,
                44);

            var claimAllReader = new GamePacketReader(
                MailPacketCodec.BuildClaimAllRequest());

            Assert.That(
                claimAllReader.Opcode,
                Is.EqualTo(GameOpcode.MailClaimAll));
            claimAllReader.EnsureFullyConsumed();
        }

        [Test]
        public void BuildListRequest_RejectsInvalidCursor()
        {
            Assert.Throws<ArgumentException>(() =>
                MailPacketCodec.BuildListRequest(
                    20,
                    new MailListCursor(
                        DateTime.SpecifyKind(SentAt, DateTimeKind.Local),
                        1)));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MailPacketCodec.BuildListRequest(
                    MailPacketCodec.MaxPageSize + 1,
                    null));
        }

        [Test]
        public void ParseListResponse_ReadsItemsAndNextCursor()
        {
            byte[] packet;

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteString("Mail list loaded");
                writer.WriteInt(1);
                writer.WriteLong(51);
                writer.WriteString("Maintenance reward");
                WriteTime(writer, SentAt);
                writer.WriteBool(true);
                WriteTime(writer, SentAt.AddDays(7));
                writer.WriteBool(false);
                writer.WriteBool(false);
                writer.WriteBool(false);
                writer.WriteBool(true);
                writer.WriteInt(2);
                writer.WriteBool(true);
                WriteTime(writer, SentAt);
                writer.WriteLong(51);
                packet = writer.BuildPacket(GameOpcode.MailListResult);
            }

            var response = MailPacketCodec.ParseListResponse(packet);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Message, Is.EqualTo("Mail list loaded"));
            Assert.That(response.Items.Count, Is.EqualTo(1));
            Assert.That(response.Items[0].Id, Is.EqualTo(51));
            Assert.That(
                response.Items[0].Title,
                Is.EqualTo("Maintenance reward"));
            Assert.That(response.Items[0].SentAt, Is.EqualTo(SentAt));
            Assert.That(
                response.Items[0].ExpiresAt,
                Is.EqualTo(SentAt.AddDays(7)));
            Assert.That(response.Items[0].CanClaim, Is.True);
            Assert.That(response.Items[0].AttachmentCount, Is.EqualTo(2));
            Assert.That(response.NextCursor.MailId, Is.EqualTo(51));
            Assert.That(response.NextCursor.SentAt, Is.EqualTo(SentAt));
        }

        [Test]
        public void ParseDetailResponse_ReadsAttachmentsAndOptionalTimes()
        {
            byte[] packet;

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteString("Mail detail loaded");
                writer.WriteLong(61);
                writer.WriteString("Emergency reward");
                writer.WriteString("Thank you for your patience.");
                WriteTime(writer, SentAt);
                writer.WriteBool(false);
                writer.WriteBool(true);
                WriteTime(writer, SentAt.AddMinutes(1));
                writer.WriteBool(false);
                writer.WriteBool(false);
                writer.WriteBool(true);
                writer.WriteInt(2);
                writer.WriteInt(1);
                writer.WriteInt(1000);
                writer.WriteInt(2);
                writer.WriteInt(500);
                packet = writer.BuildPacket(GameOpcode.MailDetailResult);
            }

            var response = MailPacketCodec.ParseDetailResponse(packet);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Mail.Id, Is.EqualTo(61));
            Assert.That(response.Mail.ExpiresAt, Is.Null);
            Assert.That(
                response.Mail.ReadAt,
                Is.EqualTo(SentAt.AddMinutes(1)));
            Assert.That(response.Mail.ClaimedAt, Is.Null);
            Assert.That(response.Mail.CanClaim, Is.True);
            Assert.That(response.Mail.Attachments.Count, Is.EqualTo(2));
            Assert.That(response.Mail.Attachments[0].RewardType, Is.EqualTo(1));
            Assert.That(response.Mail.Attachments[0].Amount, Is.EqualTo(1000));
            Assert.That(response.Mail.Attachments[1].RewardType, Is.EqualTo(2));
            Assert.That(response.Mail.Attachments[1].Amount, Is.EqualTo(500));
        }

        [Test]
        public void ParseReadResponse_ValidatesStatusAndReadTime()
        {
            byte[] packet;

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteInt((int)MailReadStatus.MarkedAsRead);
                writer.WriteString("Mail marked as read");
                writer.WriteBool(true);
                WriteTime(writer, SentAt);
                packet = writer.BuildPacket(GameOpcode.MailReadResult);
            }

            var response = MailPacketCodec.ParseReadResponse(packet);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Status, Is.EqualTo(MailReadStatus.MarkedAsRead));
            Assert.That(response.ReadAt, Is.EqualTo(SentAt));
        }

        [Test]
        public void ParseClaimResponse_ReadsCurrentBalances()
        {
            byte[] packet;

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteInt((int)MailClaimStatus.Claimed);
                writer.WriteString("Mail rewards claimed");
                writer.WriteBool(true);
                WriteTime(writer, SentAt);
                writer.WriteInt(2000);
                writer.WriteInt(750);
                packet = writer.BuildPacket(GameOpcode.MailClaimResult);
            }

            var response = MailPacketCodec.ParseClaimResponse(packet);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Status, Is.EqualTo(MailClaimStatus.Claimed));
            Assert.That(response.ClaimedAt, Is.EqualTo(SentAt));
            Assert.That(response.CurrentGold, Is.EqualTo(2000));
            Assert.That(response.CurrentGem, Is.EqualTo(750));
        }

        [Test]
        public void ParseClaimAllResponse_ReadsGrantedRewardsAndRemainingState()
        {
            byte[] packet;

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteInt((int)MailClaimAllStatus.Claimed);
                writer.WriteString("Mail rewards claimed");
                writer.WriteInt(2);
                writer.WriteInt(1500);
                writer.WriteInt(200);
                writer.WriteInt(2500);
                writer.WriteInt(700);
                writer.WriteBool(true);
                packet = writer.BuildPacket(GameOpcode.MailClaimAllResult);
            }

            var response = MailPacketCodec.ParseClaimAllResponse(packet);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Status, Is.EqualTo(MailClaimAllStatus.Claimed));
            Assert.That(response.ClaimedMailCount, Is.EqualTo(2));
            Assert.That(response.GrantedGold, Is.EqualTo(1500));
            Assert.That(response.GrantedGem, Is.EqualTo(200));
            Assert.That(response.CurrentGold, Is.EqualTo(2500));
            Assert.That(response.CurrentGem, Is.EqualTo(700));
            Assert.That(response.HasMore, Is.True);
        }

        [Test]
        public void ParseReadResponse_RejectsUndefinedStatus()
        {
            byte[] packet;

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(false);
                writer.WriteInt(999);
                writer.WriteString("Invalid status");
                writer.WriteBool(false);
                packet = writer.BuildPacket(GameOpcode.MailReadResult);
            }

            Assert.Throws<GameProtocolException>(() =>
                MailPacketCodec.ParseReadResponse(packet));
        }

        private static void AssertMailIdRequest(
            byte[] packet,
            GameOpcode opcode,
            long mailId)
        {
            var reader = new GamePacketReader(packet);

            Assert.That(reader.Opcode, Is.EqualTo(opcode));
            Assert.That(reader.ReadLong(), Is.EqualTo(mailId));
            reader.EnsureFullyConsumed();
        }

        private static void WriteTime(
            GamePacketWriter writer,
            DateTime value)
        {
            writer.WriteLong(
                new DateTimeOffset(value).ToUnixTimeMilliseconds());
        }
    }
}
