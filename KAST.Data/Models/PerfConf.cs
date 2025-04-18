namespace KAST.Data.Models
{
    public class PerfConf
    {
        public const string FILENAME = "perf.cfg"; // Filename for the performance configuration


        public ushort MaxMsgSend { get; set; } = 128; // Maximum messages sent per simulation cycle
        public ushort MaxSizeGuaranteed { get; set; } = 512; // Maximum size of guaranteed packets
        public ushort MaxSizeNonguaranteed { get; set; } = 256; // Maximum size of non-guaranteed packets

        public uint MinBandwidth { get; set; } = 131072; // Minimum bandwidth in bits per second
        public uint MaxBandwidth { get; set; } = uint.MaxValue; // Maximum bandwidth in bits per second

        public double MinErrorToSend { get; set; } = 0.001; // Minimum error to send updates
        public double MinErrorToSendNear { get; set; } = 0.01; // Minimum error to send updates for nearby objects

        public ushort MaxCustomFileSize { get; set; } = 0; // Maximum size of custom files allowed
    }
}