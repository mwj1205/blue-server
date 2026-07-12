namespace BlueServer.Client.Models
{
    public sealed class LoginResponse
    {
        public LoginResponse(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public bool Success { get; private set; }
        public string Message { get; private set; }
    }
}
