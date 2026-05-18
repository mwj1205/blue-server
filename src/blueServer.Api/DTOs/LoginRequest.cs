namespace blueServer.Api.DTOs;

public class LoginRequest
{
    public string Nickname { get; set; } = "";

    public string Password { get; set; } = "";
}