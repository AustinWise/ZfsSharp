using System;
using System.IO;

namespace ZfsSharp
{
    class Flecter4 : IChecksum
    {
        public unsafe zio_cksum_t Calculate(byte[] input)
        {
            if (input.Length % 4 != 0)
                throw new ArgumentException("Input must have a length that is a multiple of 4.");

            ulong a, b, c, d;
            a = b = c = d = 0;
            fixed (byte* ptr = input)
            {
                int size = input.Length / 4;
                uint* intPtr = (uint*)ptr;
                for (int i = 0; i < size; i++)
                {
                    a += intPtr[i];
                    b += a;
                    c += b;
                    d += c;

                }
            }

            zio_cksum_t ret = new zio_cksum_t()
            {
                word1 = a,
                word2 = b,
                word3 = c,
                word4 = d
            };
            return ret;
        }
    }
}
