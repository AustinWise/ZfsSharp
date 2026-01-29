using System;
using System.IO;
using System.IO.Compression;

namespace ZfsSharpLib
{
    class GZip : ICompression
    {
        public void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            using var inputStream = new MemoryStream(input.ToArray());
            using var zlibStream = new ZLibStream(inputStream, CompressionMode.Decompress);
            int totalRead = 0;
            while (totalRead < output.Length)
            {
                int bytesRead = zlibStream.Read(output.Slice(totalRead));
                if (bytesRead == 0)
                {
                    break; // End of stream
                }
                totalRead += bytesRead;
            }
            if (totalRead != output.Length)
            {
                throw new Exception("Decompressed data length does not match expected length.");
            }
        }
    }
}
