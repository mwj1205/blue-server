using System.Net.Sockets;
using blueServer.Game.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace blueServer.Game;

public class SessionFactory
{
    private readonly PacketDispatcher _dispatcher;

    // 세션 생성에 필요한 재료들을 팩토리가 DI를 통해 대신 관리
    public SessionFactory(PacketDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public Session Create(TcpClient client)
    {
        // 필요한 재료들을 세션 생성 시 안전하게 넘겨줌
        return new Session(client, _dispatcher);
    }
}
