using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AnOptimizationHistory
{
    public class MD5HelperStep6
    {
        private static readonly ArrayPool<byte> ByteArrayPool = ArrayPool<byte>.Shared;
        private static readonly ThreadLocal<MD5> Hasher = new ThreadLocal<MD5>(MD5.Create);

        public static string ComputeHash(string value)
        {
            var length = Encoding.UTF8.GetByteCount(value);
            var encoderBuffer = ByteArrayPool.Rent(length);

            Encoding.UTF8.GetBytes(value, encoderBuffer);

            Span<byte> hash = stackalloc byte[16];

            Hasher.Value.TryComputeHash(encoderBuffer.AsSpan(0, length), hash, out _);

            var result = ToHex(hash);
           
            ByteArrayPool.Return(encoderBuffer);

            return result;
        }

        private static string ToHex(in ReadOnlySpan<byte> bytes)
        {
            const string alphabet = "0123456789abcdef";

            Span<char> c = stackalloc char[32];

            var i = 0;
            var j = 0;

            while (i < bytes.Length)
            {
                var b = bytes[i++];
                c[j++] = alphabet[b >> 4];
                c[j++] = alphabet[b & 0xF];
            }

            var result = new string(c);

            return result;
        }
    }
}