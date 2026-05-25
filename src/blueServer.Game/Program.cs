using System.Net;
using System.Net.Sockets;
using blueServer.Game;

var listener = new TcpListener(
    IPAddress.Any,   // 모든 IP 허용
    7777             // 7777포트
);

listener.Start();

Console.WriteLine("Game Server Started");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();

    var session = new Session(client);

    _ = Task.Run(async () =>
    {
        await session.StartAsync();
    });
}
