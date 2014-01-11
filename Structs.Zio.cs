using System;
using System.Runtime.InteropServices;

namespace ZfsSharp
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct zio_cksum_t : IEquatable<zio_cksum_t>
    {
        public ulong word1;
        public ulong word2;
        public ulong word3;
        public ulong word4;

        public bool Equals(zio_cksum_t other)
        {
            return (this.word1 != other.word1 ||
                    this.word2 != other.word2 ||
                    this.word3 != other.word3 ||
                    this.word4 != other.word4);
        }

        public override int GetHashCode()
        {
            ulong longHash = word1 ^ word2 ^ word3 ^ word4;
            return (int)((longHash >> 32) ^ (longHash & 0xffffffff));
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct dva_t
    {
        long word1;
        long word2;


        public int VDev
        {
            get { return (int)(word1 >> 32); }
        }

        public int ASize
        {
            get { return (int)(word1 & 0xffffff); }
        }

        public int GRID
        {
            get { return (int)((word1 >> 24) & 0xff); }
        }

        public long Offset
        {
            get { return word2 & ~(1 << 63); }
        }

        public bool IsGang
        {
            get { return (word2 & (1 << 63)) != 0; }
        }

        public bool IsEmpty
        {
            get { return word1 == 0 && word2 == 0; }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct blkptr_t : IEquatable<blkptr_t>
    {
        public const int SPA_BLKPTRSHIFT = 7;
        public const int SPA_DVAS_PER_BP = 3;

        public dva_t dva1;
        public dva_t dva2;
        public dva_t dva3;

        long prop;

        long pad1;
        long pad2;
        public long phys_birth;
        public long birth;
        public long fill;
        public zio_cksum_t cksum;

        public int Level
        {
            get { return (int)((prop >> 56) & 0x1f); }
        }

        public bool IsLittleEndian
        {
            get { return (prop >> 63) != 0; }
        }

        public zio_checksum Checksum
        {
            get { return (zio_checksum)((prop >> 40) & 0xff); }
        }

        public zio_compress Compress
        {
            get { return (zio_compress)((prop >> 32) & 0xff); }
        }

        public dmu_object_type_t Type
        {
            get { return (dmu_object_type_t)((prop >> 48) & 0xff); }
        }

        public bool IsDedup
        {
            get { return ((prop >> 62) & 1) != 0; }
        }

        public ushort PSize
        {
            get { return (ushort)((prop >> 16) & 0xffff); }
        }

        public ushort LSize
        {
            get { return (ushort)(prop & 0xffff); }
        }

        public bool IsHole
        {
            get { return dva1.IsEmpty; }
        }

        public bool Equals(blkptr_t other)
        {
            if (this.Checksum != zio_checksum.OFF)
                return this.cksum.Equals(other.cksum);
            return false; // don't worry about blocks without checksums for now
        }

        public override bool Equals(object obj)
        {
            if (obj == null || obj.GetType() != GetType())
                return false;
            return Equals((blkptr_t)obj);
        }

        public override int GetHashCode()
        {
            return this.cksum.GetHashCode();
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
}
