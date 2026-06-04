using System.Net;
using System.Net.Sockets;
using blueServer.Game;

var listener = new TcpListener(
    IPAddress.Any,   // 모든 IP 허용
    7777             // 7777포트
);

listener.Start();
_ = Task.Run(SessionMonitor.StartAsync);

Console.WriteLine("Game Server Started on Port 7777...");

while (true)
{
    // 클라이언트 소켓 접속 수락 (비동기 블로킹)
    var client = await listener.AcceptTcpClientAsync();

    // 개별 유저 전용 세션 인스턴스 생성
    var session = new Session(client);

    // 메인 루프가 멈추지 않도록 스레드 풀에 처리를 위임하고 다음 유저 접속 대기
    _ = Task.Run(async () =>
    {
        await session.StartAsync();
    });
}
