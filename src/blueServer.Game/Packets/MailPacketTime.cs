namespace blueServer.Game.Packets;

internal static class MailPacketTime
{
    public static long ToUnixMilliseconds(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                "Mail packet timestamps must use UTC.");
        }

        return new DateTimeOffset(value).ToUnixTimeMilliseconds();
    }

    public static DateTime FromUnixMilliseconds(long value)
    {
        try
        {
            return DateTimeOffset
                .FromUnixTimeMilliseconds(value)
                .UtcDateTime;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new PacketProtocolException(
                $"Invalid mail cursor Unix time: {value}.",
                ex);
        }
    }
}
