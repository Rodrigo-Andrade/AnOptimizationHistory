using System;
using System.Security.Cryptography;
using System.Text;

namespace AnOptimizationHistory
{
    public class MD5Helper
    {
        public static string ComputeHash(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            using (var md5 = MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "");
        }
    }
}