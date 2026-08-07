using Microsoft.Extensions.Logging;

namespace blueServer.Infrastructure.Observability;

public static class LogEventIds
{
    // API 이벤트 범위: 1000~1999
    public static class Api
    {
        public static readonly EventId HttpRequestCompleted =
            new(1000, nameof(HttpRequestCompleted));

        public static readonly EventId GameRequestFailed =
            new(1001, nameof(GameRequestFailed));

        public static readonly EventId UnhandledRequestException =
            new(1002, nameof(UnhandledRequestException));

        public static readonly EventId HttpRequestCancelled =
            new(1003, nameof(HttpRequestCancelled));
    }

    // Game 이벤트 범위: 2000~2999
    public static class Game
    {
        public static readonly EventId PacketDispatchStarted =
            new(2000, nameof(PacketDispatchStarted));

        public static readonly EventId UnauthenticatedPacketRejected =
            new(2001, nameof(UnauthenticatedPacketRejected));

        public static readonly EventId UnhandledOpcodeReceived =
            new(2002, nameof(UnhandledOpcodeReceived));
    }

    public static class Orleans
    {
        public static readonly EventId PlayerProfileGrainActivated =
            new(3000, nameof(PlayerProfileGrainActivated));
    }
}
