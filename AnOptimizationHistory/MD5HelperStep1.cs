using System.Security.Cryptography;
using System.Text;

namespace AnOptimizationHistory
{
    public class MD5HelperStep1
    {
        public static string ComputeHash(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            using (var md5 = MD5.Create())
                return ToHex(md5.ComputeHash(bytes));
        }

        private static string ToHex(byte[] bytes)
        {
            const string alphabet = "0123456789abcdef";

            var c = new char[bytes.Length * 2];

            var i = 0;
            var j = 0;

            while (i < bytes.Length)
            {
                var b = bytes[i++];
                c[j++] = alphabet[b >> 4];
                c[j++] = alphabet[b & 0xF];
            }

            return new string(c);
        }
    }
}