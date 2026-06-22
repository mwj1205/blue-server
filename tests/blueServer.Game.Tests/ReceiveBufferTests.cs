using blueServer.Game;
using Xunit;

namespace blueServer.Game.Tests;

public sealed class ReceiveBufferTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Throws_WhenSizeIsNotPositive(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReceiveBuffer(size));
    }

    [Fact]
    public void Write_AppendsBytesAndUpdatesLengthAndRemainingCapacity()
    {
        var buffer = new ReceiveBuffer(8);
        var data = new byte[] { 1, 2, 3 };

        var length = buffer.Write(data, data.Length);

        Assert.Equal(3, length);
        Assert.Equal(3, buffer.Length);
        Assert.Equal(5, buffer.RemainingCapacity);
        Assert.Equal(data, buffer.Buffer.Take(3).ToArray());
    }

    [Fact]
    public void Write_Throws_WhenLengthExceedsDataLength()
    {
        var buffer = new ReceiveBuffer(8);
        var data = new byte[] { 1, 2, 3 };

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Write(data, 4));
    }

    [Fact]
    public void Write_Throws_WhenIncomingDataExceedsRemainingCapacity()
    {
        var buffer = new ReceiveBuffer(4);

        buffer.Write(new byte[] { 1, 2, 3 }, 3);

        Assert.Throws<InvalidOperationException>(() => buffer.Write(new byte[] { 4, 5 }, 2));
    }

    [Fact]
    public void Remove_ShiftsRemainingBytesToFront()
    {
        var buffer = new ReceiveBuffer(8);

        buffer.Write(new byte[] { 1, 2, 3, 4, 5 }, 5);
        buffer.Remove(2);

        Assert.Equal(3, buffer.Length);
        Assert.Equal(new byte[] { 3, 4, 5 }, buffer.Buffer.Take(3).ToArray());
    }

    [Fact]
    public void Remove_DoesNothing_WhenLengthIsZero()
    {
        var buffer = new ReceiveBuffer(8);

        buffer.Write(new byte[] { 1, 2, 3 }, 3);
        buffer.Remove(0);

        Assert.Equal(3, buffer.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer.Buffer.Take(3).ToArray());
    }

    [Fact]
    public void Remove_Throws_WhenLengthExceedsCurrentLength()
    {
        var buffer = new ReceiveBuffer(8);

        buffer.Write(new byte[] { 1, 2, 3 }, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Remove(4));
    }
}
