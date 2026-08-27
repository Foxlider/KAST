namespace KAST.Core.Models;

public class BenchmarkResult
{
    public int Parallelism { get; set; }
    public double MbPerSecond { get; set; }
    public long BytesDownloaded { get; set; }
    public double ElapsedSeconds { get; set; }
}