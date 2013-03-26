using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ZfsSharp.VirtualDevices;

namespace ZfsSharp
{
    class Zio
    {
        const int SECTOR_SIZE = 512;
        const int SPA_MINBLOCKSHIFT = 9;

        private Vdev[] mVdevs;
        private Dictionary<zio_checksum, IChecksum> mChecksums = new Dictionary<zio_checksum, IChecksum>();
        private Dictionary<zio_compress, ICompression> mCompression = new Dictionary<zio_compress, ICompression>();

        public Zio(Vdev[] vdevs)
        {
            mVdevs = vdevs;

            mChecksums.Add(zio_checksum.FLETCHER_4, new Flecter4());

            mCompression.Add(zio_compress.LZJB, new Lzjb());
            mCompression.Add(zio_compress.OFF, new NoCompression());
        }

        public byte[] Read(blkptr_t blkptr)
        {
            if (blkptr.IsDedup)
                throw new NotImplementedException("dedup not supported.");
            if (!blkptr.IsLittleEndian)
                throw new NotImplementedException("Byte swapping not implemented.");

            //try
            {
                return Read(blkptr, blkptr.dva1);
            }
            //catch
            {
                //if (blkptr.dva2.Offset == 0)
                //    throw;
                //try
                //{
                //    return Read(blkptr, blkptr.dva2);
                //}
                //catch
                //{
                //    if (blkptr.dva3.Offset == 0)
                //        throw;
                //    return Read(blkptr, blkptr.dva3);
                //}
            }
        }

        private byte[] Read(blkptr_t blkptr, dva_t dva)
        {
            if (dva.IsGang)
                throw new NotImplementedException("Gang not supported.");

            Vdev dev = mVdevs[dva.VDev];

            int physicalSize = ((int)blkptr.PSize + 1) * SECTOR_SIZE;
            foreach (byte[] physicalBytes in dev.ReadBytes(dva.Offset << 9, physicalSize))
            {
                using (var s = new MemoryStream(physicalBytes))
                {
                    var chk = mChecksums[blkptr.Checksum].Calculate(s, physicalSize);
                    if (chk.word1 != blkptr.cksum.word1 ||
                        chk.word2 != blkptr.cksum.word2 ||
                        chk.word3 != blkptr.cksum.word3 ||
                        chk.word4 != blkptr.cksum.word4)
                    {
                        Console.WriteLine("Checksum fail."); //TODO: proper logging
                        continue;
                    }
                }

                byte[] logicalBytes = new byte[((long)blkptr.LSize + 1) * SECTOR_SIZE];
                mCompression[blkptr.Compress].Decompress(physicalBytes, logicalBytes);

                return logicalBytes;
            }

            throw new Exception("Could not find a correct copy of the requested data.");
        }

        public unsafe T Get<T>(blkptr_t blkptr) where T : struct
        {
            byte[] bytes = Read(blkptr);
            fixed (byte* ptr = bytes)
                return (T)Marshal.PtrToStructure(new IntPtr(ptr), typeof(T));
        }

        class NoCompression : ICompression
        {
            public void Decompress(byte[] input, byte[] output)
            {
                Buffer.BlockCopy(input, 0, output, 0, input.Length);
            }
        }
    }

    interface IChecksum
    {
        zio_cksum_t Calculate(Stream s, long byteCount);
    }

    interface ICompression
    {
        void Decompress(byte[] input, byte[] output);
    }


    #region ZFS structs

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
    #endregion
}
