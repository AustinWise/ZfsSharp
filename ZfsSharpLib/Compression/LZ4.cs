using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    class LZ4 : ICompression
    {
        public unsafe void Decompress(ArraySegment<byte> input, ArraySegment<byte> output)
        {
            var bufsiz = input.Get(0) << 24 | input.Get(1) << 16 | input.Get(2) << 8 | input.Get(3);
            if (bufsiz + 4 > input.Count || bufsiz < 0)
                throw new ArgumentOutOfRangeException("Not enough bytes in input.");
            fixed (byte* inPtr = input.Array)
            {
                fixed (byte* outPtr = output.Array)
                {
                    int ret = Lz4Net.Lz4.LZ4_uncompress(inPtr + input.Offset + 4, outPtr + output.Offset, output.Count);
                    if (ret != bufsiz)
                        throw new Exception("Did not decompress the right number of bytes.");
                }
            }
        }
    }
}
