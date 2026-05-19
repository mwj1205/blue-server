namespace blueServer.Api.Services;

public class PasswordService
{
    // 입력된 평문 비밀번호를 복원이 불가능한 해시 문자열로 변환
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    // 유저가 입력한 비밀번호와 DB에 보관된 기존 해시값의 일치 여부를 확인
    public bool VerifyPassword(
        string password,
        string hashedPassword)
    {
        return BCrypt.Net.BCrypt.Verify(
            password,
            hashedPassword);
    }
}
