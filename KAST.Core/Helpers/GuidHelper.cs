using System.Security.Cryptography;
using System.Text;

namespace KAST.Core.Helpers
{
    public static class GuidHelper
    {
        public static Guid NewGuid(string name)
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(name));

            byte[] guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);

            // Set the version to 5 (name-based)
            guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
            // Set the variant
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

            return new Guid(guidBytes);
        }
    }
}
