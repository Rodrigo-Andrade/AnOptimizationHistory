﻿using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace AnOptimizationHistory
{
    public class MD5HelperStep3
    {
        private static readonly ArrayPool<char> CharArrayPool = ArrayPool<char>.Shared;
        private static readonly ArrayPool<byte> ByteArrayPool = ArrayPool<byte>.Shared;

        public static string ComputeHash(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);

            using (var md5 = MD5.Create())
            {
                const int hashSizeInBytes = 16;

                var buffer = ByteArrayPool.Rent(hashSizeInBytes);

                var hash = buffer.AsSpan(0, hashSizeInBytes);
                
                md5.TryComputeHash(bytes, hash, out _); //TryComputeHash can only fail if we pass a smaller buffer than needed.

                var result = ToHex(hash);

                ByteArrayPool.Return(buffer);

                return result;
            }
        }

        private static string ToHex(in ReadOnlySpan<byte> bytes)
        {
            const string alphabet = "0123456789abcdef";

            var length = bytes.Length * 2;
            var buffer = CharArrayPool.Rent(length);

            var c = buffer.AsSpan(0, length);

            var i = 0;
            var j = 0;

            while (i < bytes.Length)
            {
                var b = bytes[i++];
                c[j++] = alphabet[b >> 4];
                c[j++] = alphabet[b & 0xF];
            }

            var result = new string(c);

            CharArrayPool.Return(buffer);

            return result;
        }
    }
}