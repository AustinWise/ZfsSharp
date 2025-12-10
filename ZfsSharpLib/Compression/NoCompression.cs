using System;

namespace ZfsSharpLib
{
    class NoCompression : ICompression
    {
        public void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            input.CopyTo(output);
        }
    }
}
