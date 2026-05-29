namespace blueServer.Game;

public class ReceiveBuffer
{
    private readonly byte[] _buffer;

    private int _writePos;

    public ReceiveBuffer(int size)
    {
        _buffer = new byte[size];
    }

    // 데이터 누적
    public int Write(byte[] data, int length)
    {
        Array.Copy(
            data,
            0,
            _buffer,
            _writePos,
            length
        );

        return _writePos;
    }

    public byte[] Buffer => _buffer;

    public int Length => _writePos;

    // 처리 완료된 패킷 제거
    public void Remove(int length)
    {
        Array.Copy(
            _buffer,
            length,
            _buffer,
            0,
            _writePos - length
        );

        _writePos -= length;
    }
}