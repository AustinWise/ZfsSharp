using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System;
using System.IO;

namespace ZfsSharp
{
    class GZip : ICompression
    {
        public void Decompress(ArraySegment<byte> input, ArraySegment<byte> output)
        {
            using (var gz = new InflaterInputStream(new MemoryStream(input.Array, input.Offset, input.Count)))
            {
                int read = gz.Read(output.Array, output.Offset, output.Count);
                if (read != output.Count)
                    throw new Exception("Short read!");
            }
        }
    }
}
