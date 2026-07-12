using System;
using System.IO;
using System.Text;

namespace BlueServer.Client.Protocol
{
    public sealed class GamePacketWriter : IDisposable
    {
        public const int MaxPacketSize = 4096;

        private readonly MemoryStream _payload = new MemoryStream();

        public void WriteBool(bool value)
        {
            _payload.WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteInt(int value)
        {
            WriteLittleEndian(unchecked((uint)value), sizeof(int));
        }

        public void WriteLong(long value)
        {
            WriteLittleEndian(unchecked((ulong)value), sizeof(long));
        }

        public void WriteString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var bytes = Encoding.UTF8.GetBytes(value);

            if (bytes.Length > ushort.MaxValue)
            {
                throw new GameProtocolException(
                    "String payload exceeds the ushort length prefix.");
            }

            WriteLittleEndian((uint)bytes.Length, sizeof(ushort));
            _payload.Write(bytes, 0, bytes.Length);
        }

        public byte[] BuildPacket(GameOpcode opcode)
        {
            var payload = _payload.ToArray();
            var packetSize = sizeof(ushort) + sizeof(ushort) + payload.Length;

            if (packetSize > MaxPacketSize)
            {
                throw new GameProtocolException(
                    "Packet exceeds the maximum packet size.");
            }

            var packet = new byte[packetSize];
            WriteUInt16(packet, 0, (ushort)packetSize);
            WriteUInt16(packet, sizeof(ushort), (ushort)opcode);
            Buffer.BlockCopy(
                payload,
                0,
                packet,
                sizeof(ushort) + sizeof(ushort),
                payload.Length);

            return packet;
        }

        public void Dispose()
        {
            _payload.Dispose();
        }

        private void WriteLittleEndian(ulong value, int byteCount)
        {
            for (var index = 0; index < byteCount; index++)
            {
                _payload.WriteByte((byte)(value >> (index * 8)));
            }
        }

        private static void WriteUInt16(
            byte[] destination,
            int offset,
            ushort value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
        }
    }
}
