using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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

        //a bit of a layering violation
        public void InitMetaSlabs(objset_phys_t mos, Dmu dmu)
        {
            foreach (var v in mVdevs)
            {
                v.InitMetaSlabs(mos, dmu);
            }
        }

        public byte[] Read(blkptr_t blkptr)
        {
            if (blkptr.phys_birth != 0)
                throw new Exception("Non-zero phys birth.  This is not an error, want to see it when I read it.");

            if (blkptr.IsHole)
                throw new Exception("Block pointer is a hole.");
            if (blkptr.fill == 0)
                throw new NotSupportedException("There is no data in this block pointer.");
            if (blkptr.birth == 0)
                throw new NotSupportedException("Invalid block pointer: 0 birth txg.");
            if (blkptr.IsDedup)
                throw new NotImplementedException("dedup not supported.");
            if (blkptr.IsLittleEndian != BitConverter.IsLittleEndian)
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
            if (Marshal.SizeOf(typeof(T)) > bytes.Length)
                throw new Exception("Struct too big.");
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
}
