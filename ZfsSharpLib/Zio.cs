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
            mCompression.Add(zio_compress.LZ4, new LZ4());
        }

        //a bit of a layering violation
        public void InitMetaSlabs(objset_phys_t mos, Dmu dmu)
        {
            foreach (var v in mVdevs)
            {
                v.InitMetaSlabs(mos, dmu);
            }
        }

        unsafe byte[] ReadEmbedded(blkptr_t blkptr)
        {
            //Console.WriteLine(blkptr.EmbeddedData1[0]);
            if (blkptr.EmbedType != EmbeddedType.Data)
                throw new Exception("Unsupported embedded type: " + blkptr.EmbedType);

            int physicalSize = blkptr.PSize + 1;
            if (physicalSize > blkptr_t.EM_DATA_SIZE)
                throw new Exception("PSize is too big!");
            byte[] physicalBytes = new byte[physicalSize];

            int bytesRead = 0;

            for (int i = 0; bytesRead < physicalSize && i < blkptr_t.EM_DATA_1_SIZE; i++)
            {
                physicalBytes[bytesRead++] = blkptr.EmbeddedData1[i];
            }
            for (int i = 0; bytesRead < physicalSize && i < blkptr_t.EM_DATA_2_SIZE; i++)
            {
                physicalBytes[bytesRead++] = blkptr.EmbeddedData2[i];
            }
            for (int i = 0; bytesRead < physicalSize && i < blkptr_t.EM_DATA_3_SIZE; i++)
            {
                physicalBytes[bytesRead++] = blkptr.EmbeddedData3[i];
            }

            byte[] logicalBytes = new byte[blkptr.LSize + 1];
            mCompression[blkptr.Compress].Decompress(physicalBytes, logicalBytes);
            return logicalBytes;
        }

        public byte[] Read(blkptr_t blkptr)
        {
            if (blkptr.birth == 0)
                throw new NotSupportedException("Invalid block pointer: 0 birth txg.");
            if (blkptr.IsHole)
                throw new Exception("Block pointer is a hole.");
            if (blkptr.IsDedup)
                throw new NotImplementedException("dedup not supported.");
            if (blkptr.IsLittleEndian != BitConverter.IsLittleEndian)
                throw new NotImplementedException("Byte swapping not implemented.");

            if (blkptr.IsEmbedded)
            {
                return ReadEmbedded(blkptr);
            }

            if (blkptr.phys_birth != 0)
                throw new Exception("Non-zero phys birth.  This is not an error, want to see it when I read it.");
            if (blkptr.fill == 0)
                throw new NotSupportedException("There is no data in this block pointer.");

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
                    if (!chk.Equals(blkptr.cksum))
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
            return Program.ToStruct<T>(bytes);
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
