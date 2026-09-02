using System;
using System.Text;

namespace BlueServer.Client.Protocol
{
    public sealed class GamePacketReader
    {
        public const int HeaderSize = 4;

        private readonly byte[] _buffer;
        private int _position;

        public GamePacketReader(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (buffer.Length < HeaderSize)
            {
                throw new GameProtocolException(
                    "Packet is shorter than the protocol header.");
            }

            _buffer = buffer;

            var packetSize = ReadUInt16(buffer, 0);

            if (packetSize != buffer.Length)
            {
                throw new GameProtocolException(
                    "Packet size does not match the received buffer length.");
            }

            Opcode = (GameOpcode)ReadUInt16(buffer, sizeof(ushort));
            _position = HeaderSize;
        }

        public GameOpcode Opcode { get; private set; }

        public bool IsConsumed
        {
            get { return _position == _buffer.Length; }
        }

        public int RemainingBytes
        {
            get { return _buffer.Length - _position; }
        }

        public bool ReadBool()
        {
            EnsureAvailable(sizeof(byte));
            var value = _buffer[_position++];

            if (value > 1)
            {
                throw new GameProtocolException(
                    "Boolean payload must be encoded as zero or one.");
            }

            return value == 1;
        }

        public int ReadInt()
        {
            EnsureAvailable(sizeof(int));
            var value = unchecked((int)ReadUnsigned(sizeof(int)));
            return value;
        }

        public long ReadLong()
        {
            EnsureAvailable(sizeof(long));
            var value = unchecked((long)ReadUnsigned(sizeof(long)));
            return value;
        }

        public string ReadString()
        {
            EnsureAvailable(sizeof(ushort));
            var length = ReadUInt16(_buffer, _position);
            _position += sizeof(ushort);

            EnsureAvailable(length);

            var value = Encoding.UTF8.GetString(
                _buffer,
                _position,
                length);

            _position += length;
            return value;
        }

        public void EnsureFullyConsumed()
        {
            if (!IsConsumed)
            {
                throw new GameProtocolException(
                    "Packet contains unread payload bytes.");
            }
        }

        private ulong ReadUnsigned(int byteCount)
        {
            ulong value = 0;

            for (var index = 0; index < byteCount; index++)
            {
                value |= (ulong)_buffer[_position + index] << (index * 8);
            }

            _position += byteCount;
            return value;
        }

        private void EnsureAvailable(int byteCount)
        {
            if (byteCount < 0 || _position + byteCount > _buffer.Length)
            {
                throw new GameProtocolException(
                    "Packet payload is shorter than expected.");
            }
        }

        private static ushort ReadUInt16(byte[] source, int offset)
        {
            return (ushort)(source[offset] | source[offset + 1] << 8);
        }
    }
}
