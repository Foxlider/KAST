using KAST.Core.Hashing;

namespace KAST.Tests.Hashing;

public class Crc32Bzip2Tests
{
    [Fact]
    public void Compute_EmptyFile_ReturnsKnownValue()
    {
        var hash = Crc32Bzip2.Compute([]);
        Assert.Equal(0u, hash);
    }

    [Fact]
    public void Compute_KnownVector_MatchesExpected()
    {
        var data = "123456789"u8.ToArray();
        var hash = Crc32Bzip2.Compute(data);
        Assert.Equal(0xFC891918u, hash);
    }

    [Fact]
    public void Compute_SameInput_ProducesSameOutput()
    {
        var data = new byte[1024];
        new Random(42).NextBytes(data);
        var hash1 = Crc32Bzip2.Compute(data);
        var hash2 = Crc32Bzip2.Compute(data);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Compute_DifferentInput_ProducesDifferentOutput()
    {
        var data1 = "hello"u8.ToArray();
        var data2 = "world"u8.ToArray();
        var hash1 = Crc32Bzip2.Compute(data1);
        var hash2 = Crc32Bzip2.Compute(data2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task ComputeAsync_Stream_MatchesCompute()
    {
        var data = new byte[1024 * 1024];
        new Random(42).NextBytes(data);
        using var stream = new MemoryStream(data);
        var hash = await Crc32Bzip2.ComputeAsync(stream);
        Assert.Equal(Crc32Bzip2.Compute(data), hash);
    }

    [Fact]
    public async Task ComputeAsync_LargeFile_HandlesEfficiently()
    {
        var data = new byte[10_000_000];
        new Random(42).NextBytes(data);
        using var stream = new MemoryStream(data);
        var hash = await Crc32Bzip2.ComputeAsync(stream);
        Assert.NotEqual(0u, hash);
        Assert.Equal(Crc32Bzip2.Compute(data), hash);
    }

    [Fact]
    public void Compute_Span_MatchesByteArray()
    {
        var data = "test data for span"u8.ToArray();
        var hashBytes = Crc32Bzip2.Compute(data);
        var hashSpan = Crc32Bzip2.Compute(data.AsSpan());
        Assert.Equal(hashBytes, hashSpan);
    }
}
