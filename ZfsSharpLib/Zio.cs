using System;
using System.Collections.Generic;
using ZfsSharp.VirtualDevices;

namespace ZfsSharp
{
    class Zio
    {
        static readonly Sha256 sEmbeddedChecksum = new Sha256();

        public unsafe static bool IsEmbeddedChecksumValid(byte[] bytes, zio_cksum_t verifier)
        {
            fixed (byte* bytePtr = bytes)
            {
                zio_eck_t* pzec = (zio_eck_t*)(bytePtr + bytes.Length);
                pzec--;
                if (!pzec->IsMagicValid)
                    return false;
                var expectedChecksum = pzec->zec_cksum;
                pzec->zec_cksum = verifier;
                var actualChecksum = sEmbeddedChecksum.Calculate(new ArraySegment<byte>(bytes));
                if (!actualChecksum.Equals(expectedChecksum))
                    return false;
                pzec->zec_cksum = expectedChecksum;
                return true;
            }
        }

        const int SECTOR_SIZE = 512;
        const int SPA_MINBLOCKSHIFT = 9;

        private Vdev[] mVdevs;
        private Dictionary<zio_checksum, IChecksum> mChecksums = new Dictionary<zio_checksum, IChecksum>();
        private Dictionary<zio_compress, ICompression> mCompression = new Dictionary<zio_compress, ICompression>();

        public Zio(Vdev[] vdevs)
        {
            mVdevs = vdevs;

            mChecksums.Add(zio_checksum.OFF, new NoChecksum());
            mChecksums.Add(zio_checksum.FLETCHER_4, new Flecter4());
            mChecksums.Add(zio_checksum.SHA256, new Sha256());

            mCompression.Add(zio_compress.LZJB, new Lzjb());
            mCompression.Add(zio_compress.OFF, new NoCompression());
            mCompression.Add(zio_compress.LZ4, new LZ4());

            var gz = new GZip();
            for (int i = (int)zio_compress.GZIP_1; i <= (int)zio_compress.GZIP_9; i++)
            {
                mCompression[(zio_compress)i] = gz;
            }
        }

        void LogChecksumError()
        {
            //TODO: raise an event or something
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

            //if (blkptr.fill == 0)
            //    throw new NotSupportedException("There is no data in this block pointer.");

            //TODO: try other DVAs
            Read(blkptr, blkptr.dva1, dest);
        }

        private static zio_cksum_t CalculateGangChecksumVerifier(ref blkptr_t blkptr)
        {
            var ret = new zio_cksum_t();
            ret.word1 = (ulong)blkptr.dva1.VDev;
            ret.word2 = (ulong)(blkptr.dva1.Offset << SPA_MINBLOCKSHIFT);
            ret.word3 = (ulong)blkptr.PhysBirth;
            ret.word4 = 0;
            return ret;
        }

        private void ReadGangBlkPtr(blkptr_t blkptr, ArraySegment<byte> dest, ref int offset)
        {
            int size = LogicalSize(ref blkptr);
            Read(blkptr, new ArraySegment<byte>(dest.Array, dest.Offset + offset, size));
            offset += size;
        }

        private void Read(blkptr_t blkptr, dva_t dva, ArraySegment<byte> dest)
        {
            Vdev dev = mVdevs[dva.VDev];

            if (dva.IsGang)
            {
                if (blkptr.Compress != zio_compress.OFF)
                    throw new Exception("A compress gang block? It seems redundent to decompress twice.");

                foreach (var headerBytes in dev.ReadBytes(dva.Offset << SPA_MINBLOCKSHIFT, zio_gbh_phys_t.SPA_GANGBLOCKSIZE))
                {
                    var gangHeader = Program.ToStruct<zio_gbh_phys_t>(headerBytes);
                    if (!IsEmbeddedChecksumValid(headerBytes, CalculateGangChecksumVerifier(ref blkptr)))
                        continue;

                    int offset = 0;
                    ReadGangBlkPtr(gangHeader.zg_blkptr1, dest, ref offset);
                    ReadGangBlkPtr(gangHeader.zg_blkptr2, dest, ref offset);
                    ReadGangBlkPtr(gangHeader.zg_blkptr3, dest, ref offset);

                    var chk = mChecksums[blkptr.Checksum].Calculate(dest);
                    if (!chk.Equals(blkptr.cksum))
                    {
                        LogChecksumError();
                        continue;
                    }
                    return;
                }
            }
            else
            {
                int physicalSize = ((int)blkptr.PSize + 1) * SECTOR_SIZE;
                foreach (byte[] physicalBytes in dev.ReadBytes(dva.Offset << SPA_MINBLOCKSHIFT, physicalSize))
                {
                    var chk = mChecksums[blkptr.Checksum].Calculate(new ArraySegment<byte>(physicalBytes));
                    if (!chk.Equals(blkptr.cksum))
                    {
                        LogChecksumError();
                        continue;
                    }

                    mCompression[blkptr.Compress].Decompress(physicalBytes, dest);
                    return;
                }
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
    }

    interface IChecksum
    {
        zio_cksum_t Calculate(ArraySegment<byte> input);
    }

    interface ICompression
    {
        void Decompress(byte[] input, ArraySegment<byte> output);
    }
}
