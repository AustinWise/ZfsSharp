using System;

namespace ZfsSharp
{
    class NoCompression : ICompression
    {
        public void Decompress(byte[] input, ArraySegment<byte> output)
        {
            Buffer.BlockCopy(input, 0, output.Array, output.Offset, input.Length);
        }
    }
}
