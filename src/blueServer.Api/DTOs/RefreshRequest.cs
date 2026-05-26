namespace blueServer.Api.DTOs;

public class RefreshRequest
{
    public long PlayerId { get; set; }

    public string RefreshToken { get; set; } = "";
}
