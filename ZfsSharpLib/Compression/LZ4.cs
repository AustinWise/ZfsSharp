using System;
using K4os.Compression.LZ4;

namespace ZfsSharpLib
{
    class LZ4 : ICompression
    {
        public void Decompress(Span<byte> input, Span<byte> output)
        {
            var bufsiz = input[0] << 24 | input[1] << 16 | input[2] << 8 | input[3];
            if (bufsiz + 4 > input.Length || bufsiz < 0)
                throw new ArgumentOutOfRangeException("Not enough bytes in input.");
            int ret = LZ4Codec.Decode(input.Slice(4, bufsiz), output);
            if (ret != output.Length)
                throw new Exception("Did not decompress the right number of bytes.");
        }
    }
}
