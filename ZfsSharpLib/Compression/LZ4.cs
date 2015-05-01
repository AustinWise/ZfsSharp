using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    class LZ4 : ICompression
    {
        public unsafe void Decompress(byte[] input, byte[] output)
        {
            var bufsiz = input[0] << 24 | input[1] << 16 | input[2] << 8 | input[3];
            if (bufsiz + 4 > input.Length || bufsiz < 0)
                throw new ArgumentOutOfRangeException("Not enough bytes in input.");
            fixed (byte* inPtr = input)
            {
                fixed (byte* outPtr = output)
                {
                    int ret = Lz4Net.Lz4.LZ4_uncompress(inPtr + 4, outPtr, output.Length);
                    if (ret != bufsiz)
                        throw new Exception("Did not decompress the right number of bytes.");
                }
            }
        }
    }
}
