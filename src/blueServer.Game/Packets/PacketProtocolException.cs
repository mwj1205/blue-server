namespace blueServer.Game.Packets;

public sealed class PacketProtocolException : Exception
{
    public PacketProtocolException(string message)
        : base(message)
    {
    }

    public PacketProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
