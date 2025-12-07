using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace ZfsSharpLib
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
            return (this.word1 == other.word1 &&
                    this.word2 == other.word2 &&
                    this.word3 == other.word3 &&
                    this.word4 == other.word4);
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
    struct NormalProperties
    {
        long prop;

        public zio_checksum Checksum
        {
            get { return (zio_checksum)((prop >> 40) & 0xff); }
        }

        public zio_compress Compress
        {
            get { return (zio_compress)((prop >> 32) & 0xff); }
        }

        public ushort PSize
        {
            get { return (ushort)((prop >> 16) & 0xffff); }
        }

        public ushort LSize
        {
            get { return (ushort)(prop & 0xffff); }
        }
    }

    enum EmbeddedType : byte
    {
        Data,
        Reserved,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct EmbeddedProperties
    {
        long prop;

        public EmbeddedType EmbedType
        {
            get { return (EmbeddedType)((prop >> 40) & 0xff); }
        }

        public zio_compress Compress
        {
            get { return (zio_compress)((prop >> 32) & 0x7f); }
        }

        public ushort PSize
        {
            get { return (ushort)((prop >> 25) & 0x7f); }
        }

        public int LSize
        {
            get { return (int)(prop & 0x1ffffff); }
        }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    unsafe struct blkptr_t : IEquatable<blkptr_t>
    {
        public const int SPA_BLKPTRSHIFT = 7;
        public const int SPA_DVAS_PER_BP = 3;

        public const int EM_DATA_1_SIZE = 6 * 8;
        public const int EM_DATA_2_SIZE = 3 * 8;
        public const int EM_DATA_3_SIZE = 5 * 8;
        public const int EM_DATA_SIZE = EM_DATA_1_SIZE + EM_DATA_2_SIZE + EM_DATA_3_SIZE;

        public static readonly ImmutableArray<int> EmbeddedSizes =
            ImmutableArray.Create(EM_DATA_1_SIZE, EM_DATA_2_SIZE, EM_DATA_3_SIZE);

        [FieldOffset(0)]
        public fixed byte EmbeddedData1[EM_DATA_1_SIZE];
        [FieldOffset(7 * 8)]
        public fixed byte EmbeddedData2[EM_DATA_2_SIZE];
        [FieldOffset(0xb * 8)]
        public fixed byte EmbeddedData3[EM_DATA_3_SIZE];

        [FieldOffset(0)]
        public dva_t dva1;
        [FieldOffset(16)]
        public dva_t dva2;
        [FieldOffset(32)]
        public dva_t dva3;

        [FieldOffset(48)]
        long prop;
        [FieldOffset(48)]
        NormalProperties NormalProps;
        [FieldOffset(48)]
        EmbeddedProperties EmbedProps;

        [FieldOffset(56)]
        long pad1;
        [FieldOffset(64)]
        long pad2;
        [FieldOffset(72)]
        long phys_birth;
        [FieldOffset(80)]
        public long birth;
        [FieldOffset(88)]
        public long fill;
        [FieldOffset(96)]
        public zio_cksum_t cksum;

        public bool IsEmbedded
        {
            get { return ((prop >> 39) & 1) != 0; }
        }

        public bool IsLittleEndian
        {
            get { return (prop >> 63) != 0; }
        }

        public int Level
        {
            get { return (int)((prop >> 56) & 0x1f); }
        }

        public long PhysBirth
        {
            get
            {
                if (IsEmbedded)
                    return 0;
                if (phys_birth == 0)
                    return birth;
                return phys_birth;
            }
        }

        public EmbeddedType EmbedType
        {
            get
            {
                if (!IsEmbedded)
                    throw new Exception("EmbedType is only supported on embedded data block pointers.");
                return EmbedProps.EmbedType;
            }
        }

        public zio_checksum Checksum
        {
            get
            {
                if (IsEmbedded)
                    throw new NotSupportedException("Embedded data in block pointers does not have a checksum.");
                return NormalProps.Checksum;
            }
        }

        public zio_compress Compress
        {
            get
            {
                if (IsEmbedded)
                    return EmbedProps.Compress;
                else
                    return NormalProps.Compress;
            }
        }

        public dmu_object_type_t Type
        {
            get { return (dmu_object_type_t)((prop >> 48) & 0xff); }
        }

        public bool IsDedup
        {
            get { return ((prop >> 62) & 1) != 0; }
        }

        public int LogicalSizeBytes
        {
            get
            {
                if (IsEmbedded)
                    return EmbedProps.LSize + 1;
                else
                    return (NormalProps.LSize + 1) * Zio.SPA_MINBLOCKSIZE;
            }
        }

        public int PhysicalSizeBytes
        {
            get
            {
                if (IsEmbedded)
                    return EmbedProps.PSize + 1;
                else
                    return (NormalProps.PSize + 1) * Zio.SPA_MINBLOCKSIZE;
            }
        }

        public bool IsHole
        {
            get
            {
                return !IsEmbedded && dva1.IsEmpty;
            }
        }

        public bool Equals(blkptr_t other)
        {
            if (this.NormalProps.Checksum != zio_checksum.OFF)
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

    [StructLayout(LayoutKind.Sequential)]
    struct zio_eck_t
    {
        const UInt64 ZEC_MAGIC = 0x0210da7ab10c7a11UL;

        UInt64 zec_magic; /* for validation, endianness	*/
        public zio_cksum_t zec_cksum;  /* 256-bit checksum		*/

        public bool IsMagicValid
        {
            get
            {
                return zec_magic == ZEC_MAGIC;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct zio_gbh_phys_t
    {
        public const int SPA_GANGBLOCKSIZE = Program.SPA_MINBLOCKSIZE;
        const int SPA_GBH_FILLER = 11;

        public blkptr_t zg_blkptr1;
        public blkptr_t zg_blkptr2;
        public blkptr_t zg_blkptr3;
        fixed UInt64 zg_filler[SPA_GBH_FILLER];
        zio_eck_t zg_tail;
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
        NOPARITY,
        SHA512,
        SKEIN,
        EDONR,
        FUNCTIONS
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
        LZ4,
        ZSTD,
        FUNCTIONS
    }

    enum pool_state : ulong
    {
        /// <summary>
        /// In active use
        /// </summary>
        ACTIVE = 0,
        /// <summary>
        /// Explicitly exported
        /// </summary>
        EXPORTED,
        /// <summary>
        /// Explicitly destroyed
        /// </summary>
        DESTROYED,
        /// <summary>
        /// Reserved for hot spare use.
        /// </summary>
        SPARE,
        /// <summary>
        /// Level 2 ARC device
        /// </summary>
        L2CACHE,
    }
}
