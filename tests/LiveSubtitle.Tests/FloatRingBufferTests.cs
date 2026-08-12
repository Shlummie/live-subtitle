using LiveSubtitle.Core;

namespace LiveSubtitle.Tests;

public sealed class FloatRingBufferTests
{
    [Fact]
    public void Snapshot_PreservesOrderAfterWrapping()
    {
        var buffer = new FloatRingBuffer(5);
        buffer.Write([1, 2, 3]);
        buffer.Write([4, 5, 6, 7]);

        Assert.Equal([3, 4, 5, 6, 7], buffer.Snapshot());
    }

    [Fact]
    public void OversizedWrite_KeepsNewestSamples()
    {
        var buffer = new FloatRingBuffer(3);
        buffer.Write([1, 2, 3, 4, 5]);

        Assert.Equal([3, 4, 5], buffer.Snapshot());
    }
}
