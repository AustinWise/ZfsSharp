using System;

namespace ZfsSharp
{
    class NoCompression : ICompression
    {
        public void Decompress(Span<byte> input, Span<byte> output)
        {
            input.CopyTo(output);
        }
    }
}
