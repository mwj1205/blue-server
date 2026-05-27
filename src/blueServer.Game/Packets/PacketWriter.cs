using System.Text;

namespace blueServer.Game.Packets;

public class PacketWriter
{
    private readonly MemoryStream _stream = new();

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

    public byte[] ToArray()
    {
        return _stream.ToArray();
    }
}