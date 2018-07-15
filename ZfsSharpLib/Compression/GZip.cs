using System;

namespace ZfsSharp
{
    class GZip : ICompression
    {
        public void Decompress(Span<byte> input, Span<byte> output)
        {
            //GZip is not very common,
            //so I'm dropping support for it as part of the Span<> upgrade.
            throw new NotImplementedException();
        }
    }
}
