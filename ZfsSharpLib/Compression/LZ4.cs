using LZ4ps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    class LZ4 : ICompression
    {
        public void Decompress(Span<byte> input, Span<byte> output)
        {
            var bufsiz = input[0] << 24 | input[1] << 16 | input[2] << 8 | input[3];
            if (bufsiz + 4 > input.Length || bufsiz < 0)
                throw new ArgumentOutOfRangeException("Not enough bytes in input.");
            int ret = LZ4Codec.Decode64(input, 4, bufsiz, output, 0, output.Length, true);
            if (ret != output.Length)
                throw new Exception("Did not decompress the right number of bytes.");
        }
    }
}
