using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ZfsSharp.HardDisk;

namespace ZfsSharp
{
    class Program
    {
        const int SPA_MINBLOCKSHIFT = 9;
        const ulong UbMagic = 0x00bab10c;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct uberblock_t
        {
            public ulong Magic;
            public ulong Version;
            public ulong Txg;
            public ulong GuidSum;
            public ulong TimeStamp;
            public blkptr_t rootbp;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct zio_cksum_t
        {
            long word1;
            long word2;
            long word3;
            long word4;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct dva_t
        {
            public int VDev;
            public byte GRID;
            byte asize1;
            byte asize2;
            byte asize3;
            long offset;

            public long ASize
            {
                get
                {
                    long asize = asize1 | (asize2 << 8) | (asize3 << 16);
                    return asize << SPA_MINBLOCKSHIFT;
                }
            }

            public long Offset
            {
                get { return offset & ~(1 << 63); }
            }

            public bool IsGang
            {
                get { return (offset & (1 << 63)) != 0; }
            }
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct blkptr_t
        {
            public dva_t dva1;
            public dva_t dva2;
            public dva_t dva3;

            //long prop;
            byte lvl;
            public byte Type;
            public zio_checksum Checksum;
            public zio_compress Comp;
            public short PSIZE;
            public short LSIZE;

            long pad1;
            long pad2;
            public long phys_birth;
            public long birth;
            public long fill;
            public zio_cksum_t cksum;

            public int Level
            {
                get { return lvl & 0x1F; }
            }

            public bool IsGang
            {
                get { return (lvl & 0x80) != 0; }
            }

            public bool IsDedup
            {
                get { return (lvl & 0x40) != 0; }
            }
        }
        enum zio_checksum : byte
        {
            INHERIT = 0,
            ON,
            OFF,
            LABEL,
            GANG_HEADER,
            ZILOG,
            FLETCHER_2,
            FLETCHER_4,
            SHA256,
            ZILOG2,
        }
        enum zio_compress : byte
        {
            INHERIT = 0,
            ON,
            OFF,
            LZJB,
            EMPTY,
            GZIP_1,
            GZIP_2,
            GZIP_3,
            GZIP_4,
            GZIP_5,
            GZIP_6,
            GZIP_7,
            GZIP_8,
            GZIP_9,
            ZLE,
        }

        static void Main(string[] args)
        {
            var vhd = new VhdHardDisk(@"D:\VPC\SmartOs\SmartOs.vhd");
            var mbr = new MbrHardDisk(vhd, 0);
            var gpt = new GptHardDrive(mbr);

            List<uberblock_t> blocks = new List<uberblock_t>();
            for (long i = 0; i < 128; i++)
            {
                var offset = (128 << 10) + 1024 * i;
                uberblock_t b;
                gpt.Get<uberblock_t>(offset, out b);
                if (b.Magic == UbMagic)
                    blocks.Add(b);
            }
            var bb = blocks.OrderByDescending(u => u.Txg).First();
            Console.WriteLine();
        }
    }
}
