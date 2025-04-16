namespace KAST.Data.Models
{
    public class BasicConfig
    {
        public const string FILENAME = "basic.cfg";

        public int MinBandwidth { get; set; } = 131072;
        public long MaxBandwidth { get; set; } = 10000000000;
        public int MaxMsgSend { get; set; } = 128;
        public int MaxSizeGuaranteed { get; set; } = 512;
        public int MaxSizeNonguaranteed { get; set; } = 256;
        public double MinErrorToSend { get; set; } = 0.001;
        public double MinErrorToSendNear { get; set; } = 0.01;
        public int MaxCustomFileSize { get; set; } = 0;

    }
}