namespace KAST.Data.Models
{
    public class CfgSettings
    {
        public int MinBandwidth { get; set; }
        public long MaxBandwidth { get; set; }
        public int MaxMsgSend { get; set; }
        public int MaxSizeGuaranteed { get; set; }
        public int MaxSizeNonguaranteed { get; set; }
        public double MinErrorToSend { get; set; }
        public double MinErrorToSendNear { get; set; }
        public int MaxCustomFileSize { get; set; }
    }
}
