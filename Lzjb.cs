using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    class Lzjb : ICompression
    {
        const int NBBY = 8;

        const int MATCH_BITS = 6;
        const int MATCH_MIN = 3;
        const int MATCH_MAX = ((1 << MATCH_BITS) + (MATCH_MIN - 1));
        const int OFFSET_MASK = ((1 << (16 - MATCH_BITS)) - 1);
        const int LEMPEL_SIZE = 1024;

        public unsafe void Decompress(byte[] input, byte[] output)
        {
            fixed (byte* s_start = input)
            {
                fixed (byte* d_start = output)
                {
                    int ret = lzjb_decompress(s_start, d_start, input.LongLength, output.LongLength, 0);
                    if (ret != 0)
                        throw new Exception();
                }
            }
        }
        unsafe int lzjb_decompress(byte* s_start, byte* d_start, long s_len, long d_len, int n)
        {
            byte* src = s_start;
            byte* dst = d_start;
            byte* d_end = d_start + d_len;
            byte* cpy;
            byte copymap = 0;
            int copymask = 1 << (NBBY - 1);

            while (dst < d_end)
            {
                if ((copymask <<= 1) == (1 << NBBY))
                {
                    copymask = 1;
                    copymap = *src++;
                }
                if ((int)(copymap & copymask) != 0)
                {
                    int mlen = (src[0] >> (NBBY - MATCH_BITS)) + MATCH_MIN;
                    int offset = ((src[0] << NBBY) | src[1]) & OFFSET_MASK;
                    src += 2;
                    if ((cpy = dst - offset) < d_start)
                        return (-1);
                    while (--mlen >= 0 && dst < d_end)
                        *dst++ = *cpy++;
                }
                else
                {
                    *dst++ = *src++;
                }
            }
            return (0);
        }
    }
}
