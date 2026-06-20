namespace KAST.Core.Hashing;

public static class Crc32Bzip2
{
    private const uint Polynomial = 0x04C11DB7;
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i << 24;
            for (int j = 0; j < 8; j++)
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ Polynomial : crc << 1;
            table[i] = crc;
        }
        return table;
    }

    public static uint Compute(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
            crc = (crc << 8) ^ Table[((crc >> 24) ^ b) & 0xFF];
        return crc ^ 0xFFFFFFFF;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
            crc = (crc << 8) ^ Table[((crc >> 24) ^ b) & 0xFF];
        return crc ^ 0xFFFFFFFF;
    }

    public static async Task<uint> ComputeAsync(Stream stream, CancellationToken ct = default)
    {
        const int bufferSize = 81920;
        var buffer = new byte[bufferSize];
        uint crc = 0xFFFFFFFF;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, bufferSize, ct)) > 0)
        {
            for (int i = 0; i < bytesRead; i++)
                crc = (crc << 8) ^ Table[((crc >> 24) ^ buffer[i]) & 0xFF];
        }
        return crc ^ 0xFFFFFFFF;
    }
}
