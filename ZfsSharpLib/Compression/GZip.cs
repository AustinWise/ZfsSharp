using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System;
using System.IO;

namespace ZfsSharp
{
    class GZip : ICompression
    {
        public void Decompress(byte[] input, byte[] output)
        {
            using (var gz = new InflaterInputStream(new MemoryStream(input)))
            {
                int read = gz.Read(output, 0, output.Length);
                if (read != output.Length)
                    throw new Exception("Short read!");
            }
        }
    }
}
