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

            var gz = new GZip();
            for (int i = (int)zio_compress.GZIP_1; i <= (int)zio_compress.GZIP_9; i++)
            {
                mCompression[(zio_compress)i] = gz;
            }
        }

        //a bit of a layering violation
        public void InitMetaSlabs(ObjectSet mos)
        {
            foreach (var v in mVdevs)
            {
                v.InitMetaSlabs(mos);
            }
        }

        unsafe void ReadEmbedded(blkptr_t blkptr, ArraySegment<byte> dest)
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

            mCompression[blkptr.Compress].Decompress(physicalBytes, dest);
        }

        public void Read(blkptr_t blkptr, ArraySegment<byte> dest)
        {
            if (blkptr.birth == 0)
                throw new NotSupportedException("Invalid block pointer: 0 birth txg.");
            if (blkptr.IsHole)
                throw new Exception("Block pointer is a hole.");
            if (blkptr.IsDedup)
                throw new NotImplementedException("dedup not supported.");
            if (blkptr.IsLittleEndian != BitConverter.IsLittleEndian)
                throw new NotImplementedException("Byte swapping not implemented.");
            if (LogicalSize(ref blkptr) != dest.Count)
                throw new ArgumentOutOfRangeException("dest", "Dest does not match logical size of block pointer.");

            if (blkptr.IsEmbedded)
            {
                ReadEmbedded(blkptr, dest);
                return;
            }

            if (blkptr.phys_birth != 0)
                throw new Exception("Non-zero phys birth.  This is not an error, want to see it when I read it.");
            if (blkptr.fill == 0)
                throw new NotSupportedException("There is no data in this block pointer.");

            //TODO: try other DVAs
            Read(blkptr, blkptr.dva1, dest);
        }

        private void Read(blkptr_t blkptr, dva_t dva, ArraySegment<byte> dest)
        {
            if (dva.IsGang)
                throw new NotImplementedException("Gang not supported.");

            Vdev dev = mVdevs[dva.VDev];

            int physicalSize = ((int)blkptr.PSize + 1) * SECTOR_SIZE;
            foreach (byte[] physicalBytes in dev.ReadBytes(dva.Offset << 9, physicalSize))
            {
                var chk = mChecksums[blkptr.Checksum].Calculate(physicalBytes);
                if (!chk.Equals(blkptr.cksum))
                {
                    Console.WriteLine("Checksum fail."); //TODO: proper logging
                    continue;
                }

                mCompression[blkptr.Compress].Decompress(physicalBytes, dest);
                return;
            }

            throw new Exception("Could not find a correct copy of the requested data.");
        }

        public unsafe T Get<T>(blkptr_t blkptr) where T : struct
        {
            byte[] bytes = new byte[LogicalSize(ref blkptr)];
            Read(blkptr, new ArraySegment<byte>(bytes));
            return Program.ToStruct<T>(bytes);
        }

        public int LogicalSize(ref blkptr_t bp)
        {
            int ret = (int)(bp.LSize + 1);
            if (!bp.IsEmbedded)
                ret *= SECTOR_SIZE;
            return ret;
        }

        class NoCompression : ICompression
        {
            public void Decompress(byte[] input, ArraySegment<byte> output)
            {
                Buffer.BlockCopy(input, 0, output.Array, output.Offset, input.Length);
            }
        }
    }

    interface IChecksum
    {
        zio_cksum_t Calculate(byte[] input);
    }

    interface ICompression
    {
        void Decompress(byte[] input, ArraySegment<byte> output);
    }
}
