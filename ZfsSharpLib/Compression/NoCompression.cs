using System;

namespace ZfsSharp
{
    class NoCompression : ICompression
    {
        public void Decompress(ArraySegment<byte> input, ArraySegment<byte> output)
        {
            Buffer.BlockCopy(input.Array, input.Offset, output.Array, output.Offset, input.Count);
        }
    }
}
