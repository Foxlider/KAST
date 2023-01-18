namespace KAST.Core.Services
{
    public static class UtilitiesService
    {
        public static DateTime UnixTimeStampToDateTime(uint unixTimeStamp)
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixTimeStamp).DateTime;
        }
    }
}
