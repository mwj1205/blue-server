using System.Text;

namespace blueServer.Game.Packets;

public class PacketWriter
{
    private readonly MemoryStream _stream = new();

    public void WriteBool(bool value)
    {
        _stream.WriteByte(value ? (byte)1 : (byte)0);
    }

    public void WriteUShort(ushort value)
    {
        var bytes = BitConverter.GetBytes(value);

        _stream.Write(bytes);
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);

        WriteUShort((ushort)bytes.Length);

        _stream.Write(bytes);
    }

    public void WriteBytes(byte[] data)
    {
        _stream.Write(data);
    }

    public byte[] ToArray()
    {
        return _stream.ToArray();
    }
}
