using System.IO;

namespace ZfsSharp
{
    class Flecter4 : IChecksum
    {
        public zio_cksum_t Calculate(Stream s, long byteCount)
        {
            BinaryReader r = new BinaryReader(s);
            ulong a, b, c, d;
            a = b = c = d = 0;
            long size = byteCount / 4;
            for (int i = 0; i < size; i++)
            {
                a += r.ReadUInt32();
                b += a;
                c += b;
                d += c;
            }

            zio_cksum_t ret = new zio_cksum_t()
            {
                word1 = a,
                word2 = b,
                word3 = c,
                word4 = d
            };
            return ret;
        }
    }
}
