using System;

namespace BlueServer.Client.Protocol
{
    public sealed class GameProtocolException : Exception
    {
        public GameProtocolException(string message)
            : base(message)
        {
        }

        public GameProtocolException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
